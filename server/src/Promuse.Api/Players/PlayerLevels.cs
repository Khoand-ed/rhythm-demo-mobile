using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Api.Economy;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Config;
using Promuse.Contracts.Players;
using Promuse.Persistence;

namespace Promuse.Api.Players;

/// <summary>What a grant of EXP did: how much, the level before and after, the Originite Prime it paid.</summary>
public readonly record struct ExpGain(int Exp, int LevelBefore, int LevelAfter, int Prime);

/// <summary>
/// 玩家经验 / Player EXP and levels. A cleared ranked run pays Sanity × the remote config's
/// ExpPerSanity; every level reached pays level / 5 Originite Prime and fills Sanity to the new
/// ceiling, the way the reference game does.
/// </summary>
public sealed class PlayerLevels(PromuseDbContext db, Inventory inventory, RemoteConfigStore config, IOptions<StaminaOptions> stamina)
{
    /// <summary>
    /// 纯函数 / The arithmetic alone, pinned by tests: add the EXP, climb the curve, stop at its cap.
    /// </summary>
    public static (int Level, int Exp, int Prime) Climb(int level, int exp, int gain)
    {
        exp += Math.Max(0, gain);
        int prime = 0;

        while (true)
        {
            int need = PlayerProgression.MaxExp(level);
            if (need <= 0)
            {
                // 满级 / The curve ends: nothing more to fill.
                exp = 0;
                break;
            }

            if (exp < need) break;
            exp -= need;
            level++;
            prime += level / 5;
        }

        return (level, exp, prime);
    }

    /// <summary>
    /// Grants the EXP for a cleared run that cost <paramref name="sanity"/>. Inside the caller's
    /// transaction; the player row is locked first, so two runs closing at once both count.
    /// </summary>
    public async Task<ExpGain> GrantAsync(Guid accountId, int sanity, DateTimeOffset now, CancellationToken ct)
    {
        ConfigDocument document = (await config.GetAsync(ct)).Document;
        int gain = sanity * (document.ExpPerSanity ?? ConfigDocument.DefaultExpPerSanity);

        var player = await db.Players
            .FromSqlInterpolated($"SELECT * FROM players WHERE account_id = {accountId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);

        if (player is null || gain <= 0) return new ExpGain(0, player?.Level ?? 1, player?.Level ?? 1, 0);

        int before = player.Level;
        (int level, int exp, int prime) = Climb(player.Level, player.Exp, gain);

        if (level > before)
        {
            // 升级回满理智 / A new level fills Sanity to the new ceiling; more than that is kept.
            Stamina current = StaminaCalculator.Compute(player.Stamina, player.StaminaUpdatedAt,
                MaxStamina(before), TimeSpan.FromMinutes(stamina.Value.MinutesPerPoint), now);
            player.Stamina = Math.Max(current.Current, MaxStamina(level));
            player.StaminaUpdatedAt = now;
        }

        player.Level = level;
        player.Exp = exp;
        player.UpdatedAt = now;
        player.StateVersion++;
        await db.SaveChangesAsync(ct);

        if (prime > 0) await inventory.CreditAsync(accountId, SeedCampaign.OriginitePrimeItemId, prime, ct);

        return new ExpGain(gain, before, level, prime);
    }

    /// <summary>The ceiling, held at the last level the curve defines.</summary>
    private static int MaxStamina(int level)
    {
        int max = PlayerProgression.MaxStamina(level);
        while (max < 0 && level > 1) max = PlayerProgression.MaxStamina(--level);
        return max;
    }
}
