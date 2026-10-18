using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Api.Runs;
using Promuse.Contracts.Characters;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Characters;

/// <summary>
/// 干员升级和精英化 / Levelling an operator with battle records and LMD, and promoting it to the
/// next elite phase, which raises its level cap.
///
/// 客户端只说用什么 / The device names the records it spends, never the level it wants: the server
/// adds up their EXP and works the level out with the same OperatorLevels the level-up screen
/// previews with. The level matters beyond the roster screen - an operator's rhythm stats grow
/// with it, and a run is reviewed at it - so it is a number only the server sets.
/// </summary>
public sealed class CharacterService(
    PromuseDbContext db,
    GameData data,
    Inventory inventory,
    PlayerService players,
    TimeProvider clock)
{
    /// <summary>More of one record than this in a single level-up is a malformed request.</summary>
    public const int MaxRecordsPerKind = 999;

    public async Task<Outcome<LevelUpResult>> LevelUpAsync(
        Guid accountId, string characterId, LevelUpRequest? request, CancellationToken ct)
    {
        if (Validate(request) is { Count: > 0 } errors)
        {
            return Outcome<LevelUpResult>.Fail(ApiProblems.ValidationFailed(errors));
        }

        IReadOnlyList<ItemStack> records = request!.Records;
        DateTimeOffset now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 锁住这名干员 / The operator's row is locked before anything is decided, so two level-ups
        // of it cannot both start from the same level and EXP: the second waits, then reads what
        // the first left behind.
        PlayerCharacter? character = await db.PlayerCharacters
            .FromSqlInterpolated($"""
                SELECT * FROM player_characters
                WHERE account_id = {accountId} AND character_id = {characterId}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(ct);

        if (character is null)
        {
            return Outcome<LevelUpResult>.Fail(ApiProblems.CharacterNotOwned(characterId, "it cannot be levelled"));
        }

        OperatorRules rules = data.Rules.FindOperator(characterId)
            ?? throw new InvalidOperationException(
                $"'{characterId}' is on a roster but not in ruleset.json, so its level cap is unknown.");

        int maxLevel = OperatorLevels.MaxLevel(rules.rarity, character.Elite);
        if (character.Level >= maxLevel) return Outcome<LevelUpResult>.Fail(ApiProblems.LevelCapped(maxLevel));

        int added = records.Sum(r => r.Amount * OperatorLevels.ExpOf(r.ItemId));
        LevelGain gain = OperatorLevels.Gain(character.Elite, character.Level, character.Exp, maxLevel, added);

        // 不许整张浪费 / Spending past the cap loses the overflow, but a whole record that would
        // land entirely past it is refused: the cap is reached without it, so it is a mistake, and
        // a record cannot be got back.
        int smallest = records.Min(r => OperatorLevels.ExpOf(r.ItemId));
        if (gain.Wasted >= smallest)
        {
            return Outcome<LevelUpResult>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["records"] = [$"Level {maxLevel} is reached with fewer records; " +
                               $"{gain.Wasted} EXP would be lost past the cap."],
            }));
        }

        int lmd = OperatorLevels.LmdFor(character.Elite, gain.Applied);

        // 先收钱再收记录 / Either both are taken or neither: a refusal returns here without
        // committing, and the transaction rolls back whatever was already debited.
        if (lmd > 0 && !await inventory.TryDebitAsync(accountId, OperatorLevels.LmdItemId, lmd, ct))
        {
            return Outcome<LevelUpResult>.Fail(ApiProblems.InsufficientFunds());
        }

        foreach (ItemStack record in records)
        {
            if (!await inventory.TryDebitAsync(accountId, record.ItemId, record.Amount, ct))
            {
                return Outcome<LevelUpResult>.Fail(ApiProblems.InsufficientFunds());
            }
        }

        int from = character.Level;
        character.Level = gain.Level;
        character.Exp = gain.Exp;

        await db.SaveChangesAsync(ct);

        // 版本要动 / The roster and the bag both changed under the ETag the device is holding.
        await inventory.BumpStateVersionAsync(accountId, now, ct);

        await transaction.CommitAsync(ct);

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);
        if (!state.IsSuccess) return Outcome<LevelUpResult>.Fail(state.Problem!);

        return Outcome<LevelUpResult>.Ok(
            new LevelUpResult(characterId, from, gain.Level, gain.Applied, lmd, state.Value!));
    }

    /// <summary>
    /// 精英化 / Promotes an operator one elite phase, at its current level cap, for the LMD, Orirock
    /// and Sugar Substitute OperatorLevels.PromotionCost names. The level stays where it is - only
    /// the cap rises - so nothing about how the operator plays changes until it is levelled again.
    /// </summary>
    public async Task<Outcome<PromoteResult>> PromoteAsync(
        Guid accountId, string characterId, PromoteRequest? request, CancellationToken ct)
    {
        if (request is null || request.ToElite < 1)
        {
            return Outcome<PromoteResult>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["toElite"] = ["The elite phase to promote to, 1 or more."],
            }));
        }

        DateTimeOffset now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 同一把锁 / The same row lock as a level-up: a promotion and a level-up of one operator
        // queue behind each other rather than each starting from the phase and level before.
        PlayerCharacter? character = await db.PlayerCharacters
            .FromSqlInterpolated($"""
                SELECT * FROM player_characters
                WHERE account_id = {accountId} AND character_id = {characterId}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(ct);

        if (character is null)
        {
            return Outcome<PromoteResult>.Fail(ApiProblems.CharacterNotOwned(characterId, "it cannot be promoted"));
        }

        OperatorRules rules = data.Rules.FindOperator(characterId)
            ?? throw new InvalidOperationException(
                $"'{characterId}' is on a roster but not in ruleset.json, so its elite phases are unknown.");

        int maxElite = OperatorLevels.MaxElite(rules.rarity);
        if (character.Elite >= maxElite) return Outcome<PromoteResult>.Fail(ApiProblems.EliteCapped(maxElite));

        // 说好的那一级 / Only the next phase. A request naming any other was built from a screen that
        // had not seen the operator's current phase - most likely a promotion already sent.
        if (request.ToElite != character.Elite + 1)
        {
            return Outcome<PromoteResult>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["toElite"] = [$"The operator is at elite {character.Elite}; it can only be promoted to {character.Elite + 1}."],
            }));
        }

        int cap = OperatorLevels.MaxLevel(rules.rarity, character.Elite);
        if (character.Level < cap) return Outcome<PromoteResult>.Fail(ApiProblems.BelowLevelCap(character.Level, cap));

        IReadOnlyList<ItemStack> cost = OperatorLevels.PromotionCost(rules.rarity, request.ToElite)!;

        foreach (ItemStack item in cost)
        {
            if (!await inventory.TryDebitAsync(accountId, item.ItemId, item.Amount, ct))
            {
                return Outcome<PromoteResult>.Fail(ApiProblems.InsufficientFunds());
            }
        }

        int from = character.Elite;
        character.Elite = request.ToElite;
        character.Exp = 0;

        await db.SaveChangesAsync(ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);
        if (!state.IsSuccess) return Outcome<PromoteResult>.Fail(state.Problem!);

        return Outcome<PromoteResult>.Ok(new PromoteResult(
            characterId, from, character.Elite, character.Level,
            OperatorLevels.MaxLevel(rules.rarity, character.Elite), cost, state.Value!));
    }

    private static Dictionary<string, string[]> Validate(LevelUpRequest? request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request?.Records is not { Count: > 0 } records)
        {
            errors["records"] = ["Choose at least one battle record."];
            return errors;
        }

        var problems = new List<string>();

        foreach (ItemStack record in records)
        {
            if (record is null) problems.Add("An entry is empty.");
            else if (OperatorLevels.ExpOf(record.ItemId) == 0) problems.Add($"Item {record.ItemId} is not a battle record.");
            else if (record.Amount < 1 || record.Amount > MaxRecordsPerKind)
            {
                problems.Add($"Item {record.ItemId}: amount must be 1 to {MaxRecordsPerKind}.");
            }
        }

        problems.AddRange(records
            .Where(r => r is not null)
            .GroupBy(r => r.ItemId)
            .Where(g => g.Count() > 1)
            .Select(g => $"Item {g.Key} is listed more than once."));

        if (problems.Count > 0) errors["records"] = [.. problems];

        return errors;
    }
}
