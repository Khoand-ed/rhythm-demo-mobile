using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Missions;
using Promuse.Api.Players;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;
using Promuse.Contracts.SignIn;
using Promuse.Persistence;
using Promuse.Persistence.Entities;
using SignInTableView = Promuse.Contracts.SignIn.SignInTable;
using SignInTableRow = Promuse.Persistence.Entities.SignInTable;

namespace Promuse.Api.SignIn;

/// <summary>
/// The monthly sign-in: one square a server day, in order, starting over each month.
///
/// 和活动签到同一套 / The same rules as an event's check-in - the claim is a row keyed by the square,
/// unique per server day - so two taps on a slow connection cannot sign twice.
/// </summary>
public sealed class SignInService(
    PromuseDbContext db, TimeProvider clock, PlayerService players, Inventory inventory, MissionPeriod period)
{
    public const int MaxAmount = 1_000_000;

    public async Task<SignInMonth> MonthAsync(Guid accountId, CancellationToken ct) =>
        await BuildAsync(accountId, clock.GetUtcNow(), ct);

    public async Task<Outcome<SignInResult>> SignAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        SignInMonth month = await BuildAsync(accountId, now, ct);

        if (!month.CanSignToday)
        {
            return Fail(month.Signed >= month.Rewards.Count
                ? "Every day of this month has been signed."
                : "Today's sign-in is done. Come back tomorrow.");
        }

        int day = month.Signed + 1;
        ItemStack reward = month.Rewards[day - 1];

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.SignInClaims.Add(new SignInClaim
        {
            AccountId = accountId, MonthKey = month.MonthKey, Day = day,
            PeriodKey = period.DailyKey(now), ClaimedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 同时签了两次 / Either this square or today is already taken - a second tap racing the first.
            db.ChangeTracker.Clear();
            return Fail("Today's sign-in is done. Come back tomorrow.");
        }

        await inventory.CreditAsync(accountId, reward.ItemId, reward.Amount, ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);
        if (!state.IsSuccess) return Outcome<SignInResult>.Fail(state.Problem!);

        return Outcome<SignInResult>.Ok(
            new SignInResult(day, reward, await BuildAsync(accountId, now, ct), state.Value!), created: true);

        static Outcome<SignInResult> Fail(string detail) => Outcome<SignInResult>.Fail(ApiProblems.NothingToClaim(detail));
    }

    private async Task<SignInMonth> BuildAsync(Guid accountId, DateTimeOffset now, CancellationToken ct)
    {
        string monthKey = period.MonthlyKey(now);
        (int year, int month, int days) = period.Month(now);

        IReadOnlyList<ItemStack> table = await TableForAsync(monthKey, ct);
        List<ItemStack> rewards = [.. table.Take(days)];

        List<string> claimed = await db.SignInClaims.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.MonthKey == monthKey)
            .Select(c => c.PeriodKey)
            .ToListAsync(ct);

        bool today = claimed.Contains(period.DailyKey(now));

        return new SignInMonth(monthKey, year, month, rewards, claimed.Count,
            !today && claimed.Count < rewards.Count, period.ResetsAt(MissionTab.Daily, now), now);
    }

    /// <summary>This month's own table, or the default.</summary>
    private async Task<IReadOnlyList<ItemStack>> TableForAsync(string monthKey, CancellationToken ct)
    {
        string? json = await db.SignInTables.AsNoTracking().Where(t => t.Key == monthKey).Select(t => t.Rewards).FirstOrDefaultAsync(ct)
                       ?? await db.SignInTables.AsNoTracking().Where(t => t.Key == SeedSignIn.DefaultKey).Select(t => t.Rewards).FirstOrDefaultAsync(ct);

        return json is null ? SeedSignIn.Default() : Parse(json);
    }

    private static List<ItemStack> Parse(string json) =>
        JsonSerializer.Deserialize<List<ItemStack>>(json, SeedEvents.Json) ?? [];

    // ------------------------------------------------------------------ admin

    public async Task<SignInTables> TablesAsync(CancellationToken ct)
    {
        var rows = await db.SignInTables.AsNoTracking()
            .OrderBy(t => t.Key == SeedSignIn.DefaultKey ? 0 : 1).ThenByDescending(t => t.Key)
            .Select(t => new { Table = t, By = db.Accounts.Where(a => a.Id == t.UpdatedBy).Select(a => a.Username).FirstOrDefault() })
            .ToListAsync(ct);

        return new SignInTables([.. rows.Select(r => ToView(r.Table, r.By))]);
    }

    /// <summary>
    /// Saves the default table (exactly 31 squares) or one month's (at least as many as it has days).
    /// A month already under way can be changed: squares already claimed keep what they paid.
    /// </summary>
    public async Task<Outcome<SignInTableView>> SaveAsync(Guid adminId, string key, SignInTableRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        IReadOnlyList<ItemStack> rewards = request?.Rewards ?? [];

        int? needed = key == SeedSignIn.DefaultKey ? 31 : DaysOf(key);
        if (needed is null)
        {
            errors["key"] = ["'default' or a month as yyyy-MM."];
        }
        else if (key == SeedSignIn.DefaultKey ? rewards.Count != 31 : rewards.Count < needed || rewards.Count > 31)
        {
            errors["rewards"] = [key == SeedSignIn.DefaultKey
                ? "The default table has exactly 31 squares."
                : $"{key} has {needed} days: between {needed} and 31 squares."];
        }

        for (int i = 0; i < rewards.Count; i++)
        {
            if (rewards[i] is null || rewards[i].ItemId < 0) errors[$"rewards[{i}].itemId"] = ["Must not be negative."];
            else if (rewards[i].Amount is < 1 or > MaxAmount) errors[$"rewards[{i}].amount"] = [$"Between 1 and {MaxAmount}."];
        }

        if (errors.Count > 0) return Outcome<SignInTableView>.Fail(ApiProblems.ValidationFailed(errors));

        DateTimeOffset now = clock.GetUtcNow();
        string json = JsonSerializer.Serialize(rewards, SeedEvents.Json);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO sign_in_tables (key, rewards, updated_at, updated_by)
             VALUES ({key}, CAST({json} AS jsonb), {now}, {adminId})
             ON CONFLICT (key) DO UPDATE SET rewards = EXCLUDED.rewards, updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by
             """, ct);

        string? author = await db.Accounts.Where(a => a.Id == adminId).Select(a => a.Username).FirstOrDefaultAsync(ct);
        return Outcome<SignInTableView>.Ok(new SignInTableView(key, rewards, now, author));
    }

    /// <summary>Removes a month's own table, so it falls back to the default. The default itself stays.</summary>
    public async Task<Outcome<bool>> DeleteAsync(string key, CancellationToken ct)
    {
        if (key == SeedSignIn.DefaultKey || DaysOf(key) is null)
        {
            return Outcome<bool>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["key"] = ["Only a month's own table can be removed; the default stays."],
            }));
        }

        await db.SignInTables.Where(t => t.Key == key).ExecuteDeleteAsync(ct);
        return Outcome<bool>.Ok(true);
    }

    private static int? DaysOf(string key) =>
        DateTime.TryParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime month)
            ? DateTime.DaysInMonth(month.Year, month.Month)
            : null;

    private static SignInTableView ToView(SignInTableRow t, string? by) => new(t.Key, Parse(t.Rewards), t.UpdatedAt, by);
}
