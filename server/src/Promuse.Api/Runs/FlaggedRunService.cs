using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Leaderboards;
using Promuse.Contracts;
using Promuse.Contracts.Moderation;
using Promuse.Contracts.Runs;
using Promuse.Persistence;
using Promuse.Persistence.Entities;
using Promuse.Scoring.Review;

namespace Promuse.Api.Runs;

/// <summary>
/// 被标记的成绩 / The results the review held off the boards - a replay that did not reproduce the
/// claim, timing that looked scripted, mashing - waiting for a person to decide.
///
/// 通过就按重放的分数上榜 / Approving ranks the run on its boards (all-time, and the week it was
/// played in) at the score the server's replay came to, not the one the device claimed; with no
/// replay - an operator the ruleset did not hold - at the claim. A replay that lost the run has no
/// score to rank, and is refused.
///
/// 拒绝只是不上榜 / Rejecting keeps it off the boards for good and tells the player why by mail. The
/// clear, and what it paid, stand: the review never took them away, and a decision about a ranking
/// is not one about the rewards.
///
/// 一条锁住 / The result's row is locked for the decision, so two moderators deciding one run at once
/// cannot both rank it, or rank and reject it.
/// </summary>
public sealed class FlaggedRunService(
    PromuseDbContext db, GameData gameData, LeaderboardService leaderboards, TimeProvider clock)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxNoteLength = 200;

    /// <summary>How long the mail about a rejection stays in the box.</summary>
    public const int MailDays = 30;

    private static readonly JsonSerializerOptions ReplayJson = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// Waiting runs, oldest first - or, with <paramref name="decided"/>, the decisions already made,
    /// newest first. Only clears: a flagged loss was never going to be ranked.
    /// </summary>
    public async Task<Outcome<AdminFlaggedRunList>> ListAsync(bool decided, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return Outcome<AdminFlaggedRunList>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["limit"] = [$"Must be between 1 and {MaxLimit}."] }));
        }

        IQueryable<RunScore> flagged = db.RunScores.AsNoTracking()
            .Where(s => s.Verdict == ScoreVerdict.Flagged && s.ClaimedWon && s.Run!.Won == true);

        flagged = decided
            ? flagged.Where(s => s.Decision != null).OrderByDescending(s => s.DecidedAt)
            : flagged.Where(s => s.Decision == null).OrderBy(s => s.SubmittedAt);

        var rows = await flagged
            .Take(limit)
            .Select(s => new
            {
                Score = s,
                s.Run!.CharacterId,
                s.Run.CharacterLevel,
                Player = db.Players.Where(p => p.AccountId == s.AccountId)
                    .Select(p => new { p.DisplayName, p.Tag }).FirstOrDefault(),
                By = db.Accounts.Where(a => a.Id == s.DecidedBy).Select(a => a.Username).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Outcome<AdminFlaggedRunList>.Ok(new AdminFlaggedRunList([.. rows.Select(r => ToAdmin(
            r.Score, r.CharacterId, r.CharacterLevel, r.Player?.DisplayName ?? "", r.Player?.Tag ?? 0, r.By))]));
    }

    // ------------------------------------------------------------------ deciding

    public async Task<Outcome<AdminFlaggedRun>> ApproveAsync(
        Guid moderatorId, Guid runId, RunDecisionRequest? request, CancellationToken ct)
    {
        string? note = Note(request);
        if (note is { Length: > MaxNoteLength }) return Outcome<AdminFlaggedRun>.Fail(NoteTooLong());

        DateTimeOffset now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        (RunScore? score, Run? run, ApiProblem? problem) = await WaitingAsync(runId, ct);
        if (problem is not null) return Outcome<AdminFlaggedRun>.Fail(problem);

        ReplayResult? replay = ReadReplay(score!.Replay);
        if (replay is { Failed: true }) return Outcome<AdminFlaggedRun>.Fail(ApiProblems.RunNotRankable());

        int ranked = replay?.Score ?? score.Score;
        int combo = replay?.MaxCombo ?? score.MaxCombo;

        // 按提交的那一周 / On the boards as of when it was played: the week it was submitted in, and
        // the all-time board, each only if it beats the player's line there.
        await leaderboards.SubmitAsync(run!, ranked, combo, score.SubmittedAt, ct);

        score.Decision = RunReviewDecision.Approved;
        score.DecisionNote = note;
        score.DecidedBy = moderatorId;
        score.DecidedAt = now;
        score.RankedScore = ranked;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Outcome<AdminFlaggedRun>.Ok(await RowAsync(runId, ct));
    }

    public async Task<Outcome<AdminFlaggedRun>> RejectAsync(
        Guid moderatorId, Guid runId, RunDecisionRequest? request, CancellationToken ct)
    {
        string? note = Note(request);
        if (note is null or { Length: > MaxNoteLength })
        {
            return Outcome<AdminFlaggedRun>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["note"] = [$"1-{MaxNoteLength} characters: the player is told why."],
            }));
        }

        DateTimeOffset now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        (RunScore? score, _, ApiProblem? problem) = await WaitingAsync(runId, ct);
        if (problem is not null) return Outcome<AdminFlaggedRun>.Fail(problem);

        score!.Decision = RunReviewDecision.Rejected;
        score.DecisionNote = note;
        score.DecidedBy = moderatorId;
        score.DecidedAt = now;

        db.Mails.Add(new MailMessage
        {
            Id = Guid.NewGuid(),
            ToAccountId = score.AccountId,
            Title = "About your ranked run",
            Body = RejectionBody(gameData.ChartFor(score.StageId)?.Name ?? score.StageId, score.SubmittedAt, note),
            Sender = "Rhodes Island",
            Attachments = "[]",
            SentAt = now,
            ExpiresAt = now.AddDays(MailDays),
            SentBy = moderatorId,
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Outcome<AdminFlaggedRun>.Ok(await RowAsync(runId, ct));
    }

    // ------------------------------------------------------------------ internals

    /// <summary>The result, locked, and its run - or why it is not waiting for a decision.</summary>
    private async Task<(RunScore?, Run?, ApiProblem?)> WaitingAsync(Guid runId, CancellationToken ct)
    {
        RunScore? score = await db.RunScores
            .FromSqlInterpolated($"""
                SELECT * FROM run_scores
                WHERE run_id = {runId}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(ct);

        if (score is null) return (null, null, ApiProblems.RunNotFound());

        if (score.Verdict != ScoreVerdict.Flagged)
        {
            return (null, null, ApiProblems.RunNotInReview($"The review did not flag this run; it was {score.Verdict}."));
        }

        if (score.Decision is { } decided)
        {
            return (null, null, ApiProblems.RunNotInReview($"This run was already {decided.ToString().ToLowerInvariant()}."));
        }

        Run run = await db.Runs.AsNoTracking().FirstAsync(r => r.Id == runId, ct);

        if (!score.ClaimedWon || run.Won != true)
        {
            return (null, null, ApiProblems.RunNotInReview("Only a clear can be ranked, and this run was not one."));
        }

        return (score, run, null);
    }

    private static string? Note(RunDecisionRequest? request) =>
        string.IsNullOrWhiteSpace(request?.Note) ? null : request!.Note!.Trim();

    private static ApiProblem NoteTooLong() => ApiProblems.ValidationFailed(new Dictionary<string, string[]>
    {
        ["note"] = [$"At most {MaxNoteLength} characters."],
    });

    internal static ReplayResult? ReadReplay(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ReplayResult>(json, ReplayJson);

    private async Task<AdminFlaggedRun> RowAsync(Guid runId, CancellationToken ct)
    {
        var row = await db.RunScores.AsNoTracking()
            .Where(s => s.RunId == runId)
            .Select(s => new
            {
                Score = s,
                s.Run!.CharacterId,
                s.Run.CharacterLevel,
                Player = db.Players.Where(p => p.AccountId == s.AccountId)
                    .Select(p => new { p.DisplayName, p.Tag }).FirstOrDefault(),
                By = db.Accounts.Where(a => a.Id == s.DecidedBy).Select(a => a.Username).FirstOrDefault(),
            })
            .FirstAsync(ct);

        return ToAdmin(row.Score, row.CharacterId, row.CharacterLevel, row.Player?.DisplayName ?? "", row.Player?.Tag ?? 0, row.By);
    }

    private AdminFlaggedRun ToAdmin(RunScore s, string? characterId, int level, string name, int tag, string? by)
    {
        ReplayResult? replay = ReadReplay(s.Replay);

        return new AdminFlaggedRun(
            s.RunId, s.AccountId, name, tag, s.StageId, gameData.ChartFor(s.StageId)?.Name, characterId, level,
            s.SubmittedAt, s.Flags,
            new AdminRunNumbers(s.Score, s.MaxCombo, s.Perfect, s.Great, s.Hit, s.Miss, s.FullCombo, s.ClaimedWon),
            replay is null
                ? null
                : new AdminRunNumbers(replay.Score, replay.MaxCombo, replay.Perfect, replay.Great, replay.Hit,
                                      replay.Miss, replay.FullCombo, !replay.Failed),
            s.Ceiling, s.Presses, s.TraceFrames, s.TimingSamples, s.TimingMeanMs, s.TimingStdDevMs, s.FrameIntervalMs,
            s.Decision, s.DecisionNote, by, s.DecidedAt, s.RankedScore);
    }

    /// <summary>What the player is told: which run, that it will not be ranked, why, and that the clear stands.</summary>
    internal static string RejectionBody(string song, DateTimeOffset playedAt, string reason) =>
        $"Doctor,\n\nyour ranked run of {song} on {playedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC has been reviewed " +
        $"by a moderator and will not be ranked.\n\nReason: {reason}\n\nThe clear and everything it paid stand.";
}
