using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Api.Infrastructure;
using Promuse.Api.Leaderboards;
using Promuse.Api.Missions;
using Promuse.Api.Players;
using Promuse.Contracts.Players;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Runs;
using Promuse.Persistence;
using Promuse.Persistence.Entities;
using Promuse.Scoring.Review;

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
    LeaderboardService leaderboards,
    GameData gameData,
    IOptions<StaminaOptions> stamina)
{
    public async Task<Outcome<RunTicket>> StartAsync(
        Guid accountId, string stageId, string characterId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(stageId)) errors["stageId"] = ["Required."];
        if (string.IsNullOrWhiteSpace(characterId)) errors["characterId"] = ["Required."];

        if (errors.Count > 0) return Outcome<RunTicket>.Fail(ApiProblems.ValidationFailed(errors));

        Stage? stage = await db.Stages
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StageId == stageId && s.IsActive, ct);

        if (stage is null) return Outcome<RunTicket>.Fail(ApiProblems.StageNotFound(stageId));

        // 只能用自己有的 / Only an operator the player owns. Their modifiers are part of what the
        // result is checked against, so this is the check that makes the operator on the run
        // mean anything: otherwise anyone could open every run with the strongest one.
        bool owned = await db.PlayerCharacters
            .AnyAsync(c => c.AccountId == accountId && c.CharacterId == characterId, ct);

        if (!owned) return Outcome<RunTicket>.Fail(ApiProblems.CharacterNotOwned(characterId));

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
            CharacterId = characterId,
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
    /// Closes a run that this player opened and has not closed, and decides what its result is
    /// worth.
    ///
    /// 找不到就拒绝 / The lookup is by run id AND account AND still-open, so a run
    /// that belongs to someone else is indistinguishable from one that does not
    /// exist. Answering differently would let a stranger probe which run ids are
    /// real.
    ///
    /// 先审再写 / The review runs before the transaction opens: it is CPU work over the chart
    /// and the trace, and holding row locks through it would serialise every completion
    /// behind the slowest one. The run is then closed with the same conditional UPDATE as
    /// before, so of two completions racing for one run exactly one gets to write anything.
    ///
    /// 一个事务 / Closing the run, storing the result, the mission counters and the
    /// leaderboards commit together. A run is never ranked without being closed, nor counted
    /// as a clear without its result on record.
    ///
    /// 被拒就不算通关 / A rejected result is not a clear: no mission progress, no ranking.
    /// Stamina is spent either way - it was spent when the run opened.
    /// </summary>
    public async Task<Outcome<RunCompletion>> CompleteAsync(
        Guid accountId, Guid runId, CompleteRunRequest request, CancellationToken ct)
    {
        Outcome<Submission?> parsed = Parse(request);
        if (!parsed.IsSuccess) return Outcome<RunCompletion>.Fail(parsed.Problem!);

        Submission? submission = parsed.Value;

        Run? run = await db.Runs
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.AccountId == accountId && r.CompletedAt == null, ct);

        if (run is null) return Outcome<RunCompletion>.Fail(ApiProblems.RunNotOpen());

        DateTimeOffset now = clock.GetUtcNow();
        ReviewResult? review = submission is null ? null : Review(run, request.Won, submission, now);

        bool countsAsClear = request.Won && review is not null && review.Verdict != ScoreVerdict.Rejected;
        bool ranked = countsAsClear && review!.Verdict == ScoreVerdict.Accepted;
        bool personalBest = false;

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // 条件更新就是防重 / A conditional UPDATE rather than read-then-write: two
            // completions racing for one run both read it open above, and exactly one changes
            // a row here. The other is told the run is not open, which is by then true.
            int closed = await db.Runs
                .Where(r => r.Id == runId && r.AccountId == accountId && r.CompletedAt == null)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(r => r.CompletedAt, now)
                    .SetProperty(r => r.Won, countsAsClear), ct);

            if (closed == 0) return Outcome<RunCompletion>.Fail(ApiProblems.RunNotOpen());

            if (submission is not null && review is not null)
            {
                db.RunScores.Add(ToRecord(run, request.Won, submission, review, now));
                await db.SaveChangesAsync(ct);
            }

            if (countsAsClear) await missions.NotifyAsync(accountId, MissionGoal.PlaySong, ct);

            if (ranked)
            {
                personalBest = await leaderboards.SubmitAsync(
                    run, submission!.Result.Score, submission.Result.MaxCombo, now, ct);
            }

            await transaction.CommitAsync(ct);
        }

        ScoreReview? answer = null;

        if (review is not null)
        {
            int? allTime = null, weekly = null;

            if (ranked)
            {
                allTime = await leaderboards.RankOfAsync(accountId, run.StageId, LeaderboardService.AllTimeKey, ct);
                weekly = await leaderboards.RankOfAsync(
                    accountId, run.StageId, leaderboards.KeyFor(Contracts.Leaderboards.LeaderboardPeriod.Weekly, now), ct);
            }

            answer = new ScoreReview(
                review.Verdict,
                review.Verdict switch
                {
                    ScoreVerdict.Rejected => review.Reasons,
                    ScoreVerdict.Flagged => [ScoreReviewCodes.UnderReview],
                    _ => [],
                },
                submission!.Result.Score,
                personalBest,
                allTime,
                weekly);
        }

        Outcome<MissionBoards> boards = await missions.GetBoardsAsync(accountId, ct);
        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!boards.IsSuccess) return Outcome<RunCompletion>.Fail(boards.Problem!);
        if (!state.IsSuccess) return Outcome<RunCompletion>.Fail(state.Problem!);

        return Outcome<RunCompletion>.Ok(new RunCompletion(
            runId, countsAsClear, answer, boards.Value!, state.Value!, now));
    }

    /// <summary>A result that parsed, with its trace unwrapped.</summary>
    private sealed record Submission(RunResult Result, byte[]? CompressedTrace, byte[]? RawTrace);

    /// <summary>
    /// 格式错误和说谎不一样 / Malformed is not the same as dishonest. A negative count or a
    /// missing field is a broken request and gets a 422 before anything is stored; a
    /// well-formed result that cannot be true is stored and answered with a verdict. The line
    /// between them is whether the result could even be written down.
    /// </summary>
    private static Outcome<Submission?> Parse(CompleteRunRequest request)
    {
        RunResult? result = request.Result;

        if (result is null)
        {
            if (!request.Won) return Outcome<Submission?>.Ok(null);

            return Outcome<Submission?>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["result"] = ["Required when won is true: a clear is counted only once its result is checked."],
            }));
        }

        var errors = new Dictionary<string, string[]>();

        if (result.Score < 0) errors["result.score"] = ["Must not be negative."];
        if (result.MaxCombo < 0) errors["result.maxCombo"] = ["Must not be negative."];
        if (result.Perfect < 0 || result.Great < 0 || result.Hit < 0 || result.Miss < 0)
        {
            errors["result.counts"] = ["Must not be negative."];
        }

        if (string.IsNullOrEmpty(result.RulesetFingerprint) || result.RulesetFingerprint.Length > 32)
        {
            errors["result.rulesetFingerprint"] = ["Required, at most 32 characters."];
        }

        if (result.Trace is { Length: > TraceCodec.MaxEncodedChars })
        {
            errors["result.trace"] = [$"At most {TraceCodec.MaxEncodedChars} characters."];
        }

        if (errors.Count > 0) return Outcome<Submission?>.Fail(ApiProblems.ValidationFailed(errors));

        if (string.IsNullOrEmpty(result.Trace)) return Outcome<Submission?>.Ok(new Submission(result, null, null));

        (byte[] compressed, byte[] raw) = TraceCodec.Unpack(result.Trace);
        return Outcome<Submission?>.Ok(new Submission(result, compressed, raw));
    }

    private ReviewResult Review(Run run, bool won, Submission submission, DateTimeOffset now)
    {
        Scoring.Rules.ScoringChart? chart = gameData.ChartFor(run.StageId);

        // 无从核对 / A stage the server has no chart for cannot be checked at all. That is the
        // server's gap, not the player's, so the clear stands and only the ranking is withheld.
        if (chart is null)
        {
            return new ReviewResult(ScoreVerdict.Flagged, [], [ReviewFlags.Unverifiable], null, null, 0, 0);
        }

        RunResult result = submission.Result;

        return RunReview.Evaluate(new ReviewInput
        {
            Chart = chart,
            Rules = gameData.Rules,
            Operator = gameData.Rules.FindOperator(run.CharacterId),
            Won = won,
            Score = result.Score,
            MaxCombo = result.MaxCombo,
            Perfect = result.Perfect,
            Great = result.Great,
            Hit = result.Hit,
            Miss = result.Miss,
            FullCombo = result.FullCombo,
            Fingerprint = result.RulesetFingerprint,
            Trace = submission.RawTrace,
            Elapsed = now - run.StartedAt,
        });
    }

    private static RunScore ToRecord(Run run, bool claimedWon, Submission submission, ReviewResult review, DateTimeOffset now)
    {
        RunResult result = submission.Result;

        return new RunScore
        {
            RunId = run.Id,
            AccountId = run.AccountId,
            StageId = run.StageId,
            ClaimedWon = claimedWon,
            Score = result.Score,
            MaxCombo = result.MaxCombo,
            Perfect = result.Perfect,
            Great = result.Great,
            Hit = result.Hit,
            Miss = result.Miss,
            FullCombo = result.FullCombo,
            Verdict = review.Verdict,
            Reasons = [.. review.Reasons],
            Flags = [.. review.Flags],
            Ceiling = review.Ceiling,
            RulesetFingerprint = result.RulesetFingerprint,
            Trace = submission.CompressedTrace,
            TraceFrames = review.TraceFrames,
            Presses = review.Presses,
            TimingSamples = review.Timing?.Samples,
            TimingMeanMs = review.Timing?.MeanMs,
            TimingStdDevMs = review.Timing?.StdDevMs,
            FrameIntervalMs = review.Timing?.FrameIntervalMs,
            SubmittedAt = now,
        };
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
