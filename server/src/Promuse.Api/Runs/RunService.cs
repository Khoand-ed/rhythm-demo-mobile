using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Api.Infrastructure;
using Promuse.Api.Missions;
using Promuse.Api.Players;
using Promuse.Contracts.Players;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Runs;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Runs;

/// <summary>
/// Opens an attempt at a song: charges the stamina and issues the seed.
///
/// 为什么是"开一局"而不是"扣理智" / A lifecycle rather than a debit, because the
/// far end of it is Phase 4. A submitted score can only be trusted if the server
/// already knows a run was opened, on which chart, what it cost, and with which
/// random sequence. An endpoint that only spent stamina would have to be
/// redesigned into this the moment results arrive.
/// </summary>
public sealed class RunService(
    PromuseDbContext db,
    TimeProvider clock,
    PlayerService players,
    MissionService missions,
    IOptions<StaminaOptions> stamina)
{
    public async Task<Outcome<RunTicket>> StartAsync(Guid accountId, string stageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stageId))
        {
            return Outcome<RunTicket>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["stageId"] = ["Required."] }));
        }

        Stage? stage = await db.Stages
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StageId == stageId && s.IsActive, ct);

        if (stage is null) return Outcome<RunTicket>.Fail(ApiProblems.StageNotFound(stageId));

        Player? player = await db.Players.FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

        if (player is null) return Outcome<RunTicket>.Fail(ApiProblems.Unauthorized());

        DateTimeOffset now = clock.GetUtcNow();

        // 先结算再扣 / The bar has to be brought up to date before anything is
        // taken from it: what is stored is what was true at StaminaUpdatedAt, and
        // the points earned since then are as real as the ones already there.
        Stamina current = StaminaCalculator.Compute(
            player.Stamina,
            player.StaminaUpdatedAt,
            PlayerProgression.MaxStamina(player.Level),
            TimeSpan.FromMinutes(stamina.Value.MinutesPerPoint),
            now);

        if (current.Current < stage.StaminaCost)
        {
            return Outcome<RunTicket>.Fail(
                ApiProblems.InsufficientStamina(current.Current, stage.StaminaCost));
        }

        player.Stamina = current.Current - stage.StaminaCost;

        // 锚点移到现在 / The anchor moves to now, so regeneration restarts from
        // this moment rather than from whenever the bar was last written. Leaving
        // it where it was would hand back the elapsed time a second time.
        player.StaminaUpdatedAt = now;
        player.UpdatedAt = now;

        // StateVersion is the concurrency token, so two starts racing for the
        // last few points cannot both succeed: the second SaveChanges matches no
        // row and throws.
        player.StateVersion++;

        var run = new Run
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            StageId = stage.StageId,
            StaminaSpent = stage.StaminaCost,
            Seed = NewSeed(),
            StartedAt = now,
        };

        db.Runs.Add(run);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();

            // Another run opened between the read and the write. The player is
            // told to try again rather than being charged twice for one bar.
            return Outcome<RunTicket>.Fail(ApiProblems.StateConflict(0));
        }

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!state.IsSuccess) return Outcome<RunTicket>.Fail(state.Problem!);

        return Outcome<RunTicket>.Ok(new RunTicket(
            run.Id, run.StageId, run.Seed, run.StaminaSpent, state.Value!, now));
    }

    /// <summary>
    /// Closes a run that this player opened and has not closed.
    ///
    /// 找不到就拒绝 / The lookup is by run id AND account AND still-open, so a run
    /// that belongs to someone else is indistinguishable from one that does not
    /// exist. Answering differently would let a stranger probe which run ids are
    /// real.
    ///
    /// 只有通关才计数 / Only a win advances the missions, which is what
    /// GameManager already did: a run that ran out of HP is not a clear. The
    /// stamina is spent either way - it was spent when the run opened.
    /// </summary>
    public async Task<Outcome<RunCompletion>> CompleteAsync(
        Guid accountId, Guid runId, bool won, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 条件更新就是防重 / A conditional UPDATE rather than read-then-write:
        // two completions racing for one run both see it open, and exactly one
        // changes a row. The other is told the run is not open, which is true.
        int closed = await db.Runs
            .Where(r => r.Id == runId && r.AccountId == accountId && r.CompletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CompletedAt, now)
                .SetProperty(r => r.Won, won), ct);

        if (closed == 0) return Outcome<RunCompletion>.Fail(ApiProblems.RunNotOpen());

        if (won) await missions.NotifyAsync(accountId, MissionGoal.PlaySong, ct);

        await transaction.CommitAsync(ct);

        Outcome<MissionBoards> boards = await missions.GetBoardsAsync(accountId, ct);
        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!state.IsSuccess) return Outcome<RunCompletion>.Fail(state.Problem!);

        return Outcome<RunCompletion>.Ok(new RunCompletion(
            runId, won, boards.Value!, state.Value!, now));
    }

    /// <summary>
    /// 必须不可预测 / From the CSPRNG rather than Random, because a predictable
    /// seed is one a client can work out in advance - and knowing the draw ahead
    /// of time is most of the value of cheating at a passive that rolls.
    ///
    /// Masked to non-negative so it reads plainly in a log and in JSON; that
    /// leaves 63 bits, which is not a meaningful reduction.
    /// </summary>
    private static long NewSeed() =>
        BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & long.MaxValue;
}
