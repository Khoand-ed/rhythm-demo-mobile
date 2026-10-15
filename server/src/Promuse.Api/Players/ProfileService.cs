using Microsoft.EntityFrameworkCore;
using Promuse.Api.Campaign;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Players;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Players;

/// <summary>
/// 名片 / A player's profile card: who they are, how far they have come, and how they play.
///
/// 只读, 谁都能看 / Read-only and open to every signed-in player, because the leaderboards already
/// name everyone on them and a name is where a card is opened from. Nothing private is read here -
/// no bag, no stamina, no account details beyond the day it was made.
///
/// 每次现算 / Counted on every read rather than kept as totals. A card is opened now and then, the
/// counts are a handful of indexed queries over one player's rows, and a total kept elsewhere is one
/// more thing a run's completion would have to remember to update.
/// </summary>
public sealed class ProfileService(PromuseDbContext db, CampaignService campaign)
{
    public async Task<Outcome<PlayerProfile>> GetAsync(Guid viewerId, Guid playerId, CancellationToken ct)
    {
        // 封号的人不给看 / A banned player's card is not shown to others - it is reported as not
        // there, the same as an id that never existed. Their own still opens for them.
        var row = await db.Players.AsNoTracking()
            .Where(p => p.AccountId == playerId && (p.Account!.BannedAt == null || p.AccountId == viewerId))
            .Select(p => new
            {
                p.AccountId,
                p.DisplayName,
                p.Tag,
                p.Level,
                p.Signature,
                p.DesktopCharacterId,
                p.CreatedAt,
                Squad = p.Squad.OrderBy(s => s.Slot).Select(s => s.CharacterId).ToList(),
                Owned = p.Characters
                    .Select(c => new PlayerProfiles.Owned(c.CharacterId, c.Elite, c.Level, c.Trust))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return Outcome<PlayerProfile>.Fail(ApiProblems.PlayerNotFound());

        return Outcome<PlayerProfile>.Ok(new PlayerProfile(
            PlayerId: row.AccountId,
            DisplayName: row.DisplayName,
            Tag: row.Tag,
            Level: row.Level,
            Signature: row.Signature,
            AssistantId: PlayerProfiles.AssistantOf(row.DesktopCharacterId, row.Squad, row.Owned),
            RegisteredAt: row.CreatedAt,
            CampaignProgress: await campaign.FurthestClearedAsync(playerId, ct),
            Operators: row.Owned.Count,
            Rhythm: await RhythmAsync(playerId, ct),
            IsSelf: playerId == viewerId));
    }

    /// <summary>
    /// 只数服务端认的 / Counts only what the server accepted. A clear is a run it closed as won,
    /// which already leaves out a rejected result; a best score is a ranked one, so it is the same
    /// number the player's best all-time board line shows.
    /// </summary>
    private async Task<RhythmRecord> RhythmAsync(Guid accountId, CancellationToken ct)
    {
        int plays = await db.Runs
            .CountAsync(r => r.AccountId == accountId && r.CompletedAt != null, ct);

        int cleared = await db.Runs
            .Where(r => r.AccountId == accountId && r.Won == true)
            .Select(r => r.StageId)
            .Distinct()
            .CountAsync(ct);

        int fullCombos = await db.RunScores
            .Where(s => s.AccountId == accountId && s.FullCombo && s.Run!.Won == true)
            .Select(s => s.StageId)
            .Distinct()
            .CountAsync(ct);

        int? best = await db.RunScores
            .Where(s => s.AccountId == accountId && s.Verdict == ScoreVerdict.Accepted)
            .MaxAsync(s => (int?)s.Score, ct);

        return new RhythmRecord(plays, cleared, fullCombos, best);
    }
}
