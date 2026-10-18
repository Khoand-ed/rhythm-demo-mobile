using System.Net;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Leaderboards;
using Promuse.Contracts.Mail;
using Promuse.Contracts.Moderation;
using Promuse.Contracts.Players;
using Promuse.Contracts.Runs;
using Promuse.Scoring.Review;

namespace Promuse.Api.Tests;

/// <summary>What both moderation queues need: a player who writes a line, and a player who plays a run.</summary>
public abstract class ReviewQueueTestBase(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    protected async Task<HttpResponseMessage> WriteLineAsync(AuthSession player, string? line)
    {
        HttpResponseMessage state = await Client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", player));
        return await Client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/signature", player,
            new SignatureRequest(line), state.Headers.ETag!.ToString()));
    }

    protected async Task<MailBox> BoxAsync(AuthSession player) =>
        await ReadAsync<MailBox>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/mail", player)));

    /// <summary>Opens a ranked run on the stage the fixtures play, ages it past the chart, and closes it with <paramref name="result"/>.</summary>
    protected async Task<(Guid RunId, RunCompletion Done)> PlayAsync(AuthSession player, RunResult result)
    {
        HttpResponseMessage start = await Client.SendAsync(Send(HttpMethod.Post, "/v1/runs", player,
            new StartRunRequest(Played.Stage, Played.Operator)));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        RunTicket ticket = await ReadAsync<RunTicket>(start);

        await Played.AgeAsync(Factory, ticket.RunId, TimeSpan.FromMinutes(5));

        HttpResponseMessage done = await Client.SendAsync(Send(HttpMethod.Post, $"/v1/runs/{ticket.RunId}/complete", player,
            new CompleteRunRequest(true, result)));
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        return (ticket.RunId, await ReadAsync<RunCompletion>(done));
    }
}

// ================================================================================ flagged runs

[Collection(ApiCollection.Name)]
public class FlaggedRunEndpointTests(PromuseApiFactory factory) : ReviewQueueTestBase(factory)
{
    private async Task<AdminFlaggedRunList> ListAsync(AuthSession admin, bool decided = false) =>
        await ReadAsync<AdminFlaggedRunList>(await Client.SendAsync(
            Send(HttpMethod.Get, $"/v1/admin/runs/flagged?decided={decided}&limit=200", admin)));

    private async Task<HttpResponseMessage> DecideAsync(AuthSession admin, Guid runId, string decision, string? note) =>
        await Client.SendAsync(Send(HttpMethod.Post, $"/v1/admin/runs/{runId}/{decision}", admin, new RunDecisionRequest(note)));

    private async Task<LeaderboardEntry?> MyLineAsync(AuthSession player) =>
        (await ReadAsync<LeaderboardPage>(await Client.SendAsync(
            Send(HttpMethod.Get, $"/v1/leaderboards/{Played.Stage}?period=AllTime&limit=1", player)))).Me;

    /// <summary>A clear whose claimed score is padded: consistent, under the ceiling, and flagged by the replay.</summary>
    private async Task<(Guid RunId, RunResult Honest)> PaddedAsync(AuthSession player)
    {
        RunResult honest = Played.Win(Factory, misses: 6);
        (Guid runId, RunCompletion done) = await PlayAsync(player, honest with { Score = honest.Score + 777 });
        Assert.Equal(ScoreVerdict.Flagged, done.Review!.Verdict);
        return (runId, honest);
    }

    [Fact]
    public async Task Only_administrators_decide_runs()
    {
        (AuthSession player, _) = await PlayerAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/runs/flagged", player))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(player, Guid.NewGuid(), "approve", null)).StatusCode);
    }

    [Fact]
    public async Task A_flagged_run_waits_with_its_claim_and_its_replay_side_by_side()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, string name) = await PlayerAsync();
        (Guid runId, RunResult honest) = await PaddedAsync(player);

        AdminFlaggedRun waiting = Assert.Single((await ListAsync(admin)).Runs, r => r.RunId == runId);

        Assert.Equal((name, Played.Stage, "hung"), (waiting.DisplayName, waiting.StageId, waiting.SongName));
        Assert.Equal(new[] { ReviewFlags.ReplayMismatch }, waiting.Flags);
        Assert.Equal(honest.Score + 777, waiting.Claimed.Score);
        Assert.Equal(honest.Score, waiting.Replay!.Score);
        Assert.Equal((honest.Perfect, true), (waiting.Replay.Perfect, waiting.Replay.Won));
        Assert.Null(waiting.Decision);
    }

    [Fact]
    public async Task Approving_ranks_the_run_at_the_score_its_replay_came_to()
    {
        (AuthSession admin, string adminName) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        (Guid runId, RunResult honest) = await PaddedAsync(player);
        Assert.Null(await MyLineAsync(player));

        HttpResponseMessage approved = await DecideAsync(admin, runId, "approve", "Checked the trace");

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        AdminFlaggedRun decided = await ReadAsync<AdminFlaggedRun>(approved);
        Assert.Equal((RunReviewDecision.Approved, honest.Score, adminName), (decided.Decision!.Value, decided.RankedScore, decided.DecidedBy));

        // On the board, and on the card, at the replay's score - never the padded claim.
        Assert.Equal(honest.Score, (await MyLineAsync(player))!.Score);
        PlayerProfile card = await ReadAsync<PlayerProfile>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/players/me/profile", player)));
        Assert.Equal(honest.Score, card.Rhythm.BestScore);

        Assert.DoesNotContain((await ListAsync(admin)).Runs, r => r.RunId == runId);
        Assert.Contains((await ListAsync(admin, decided: true)).Runs, r => r.RunId == runId);

        // Decided once.
        HttpResponseMessage again = await DecideAsync(admin, runId, "reject", "Changed my mind");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.RunNotInReview, await CodeAsync(again));
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_keeps_the_run_off_the_board_and_tells_the_player_why()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        (Guid runId, _) = await PaddedAsync(player);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await DecideAsync(admin, runId, "reject", "  ")).StatusCode);

        HttpResponseMessage rejected = await DecideAsync(admin, runId, "reject", "The score does not match the input");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(RunReviewDecision.Rejected, (await ReadAsync<AdminFlaggedRun>(rejected)).Decision);

        Assert.Null(await MyLineAsync(player));

        MailItem mail = Assert.Single((await BoxAsync(player)).Mails, m => m.Title == "About your ranked run");
        Assert.Contains("hung", mail.Body);
        Assert.Contains("Reason: The score does not match the input", mail.Body);
        Assert.Contains("The clear and everything it paid stand.", mail.Body);
    }

    [Fact]
    public async Task A_run_the_review_accepted_or_one_that_does_not_exist_is_not_in_review()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        (Guid accepted, RunCompletion done) = await PlayAsync(player, Played.Win(Factory, misses: 6));
        Assert.Equal(ScoreVerdict.Accepted, done.Review!.Verdict);

        HttpResponseMessage notFlagged = await DecideAsync(admin, accepted, "approve", null);
        Assert.Equal(HttpStatusCode.Conflict, notFlagged.StatusCode);
        Assert.Equal(ErrorCodes.RunNotInReview, await CodeAsync(notFlagged));

        HttpResponseMessage nothing = await DecideAsync(admin, Guid.NewGuid(), "approve", null);
        Assert.Equal(HttpStatusCode.NotFound, nothing.StatusCode);
        Assert.Equal(ErrorCodes.RunNotFound, await CodeAsync(nothing));
    }
}

// ================================================================================ signature reports

[Collection(ApiCollection.Name)]
public class SignatureReportEndpointTests(PromuseApiFactory factory) : ReviewQueueTestBase(factory)
{
    private async Task<HttpResponseMessage> ReportAsync(AuthSession reporter, Guid target,
                                                        SignatureReportReason reason = SignatureReportReason.Spam, string? note = null) =>
        await Client.SendAsync(Send(HttpMethod.Post, $"/v1/players/{target}/signature/report", reporter,
            new SignatureReportRequest(reason, note)));

    private async Task<AdminReportQueue> QueueAsync(AuthSession admin) =>
        await ReadAsync<AdminReportQueue>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/signatures/reports", admin)));

    [Fact]
    public async Task A_player_reports_someone_elses_line_once()
    {
        (AuthSession target, _) = await PlayerAsync();
        (AuthSession reporter, _) = await PlayerAsync();
        (AuthSession quiet, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteLineAsync(target, "cheap orundum here")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(reporter, target.PlayerId, note: "selling things")).StatusCode);

        HttpResponseMessage again = await ReportAsync(reporter, target.PlayerId, SignatureReportReason.Offensive);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.AlreadyReported, await CodeAsync(again));

        // Not your own, not a line that is not there, not a player who is not there.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReportAsync(target, target.PlayerId)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReportAsync(reporter, quiet.PlayerId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(reporter, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await ReportAsync(quiet, target.PlayerId, note: new string('x', SignatureReportLimits.NoteMaxLength + 1))).StatusCode);
    }

    [Fact]
    public async Task A_player_may_send_only_so_many_reports_a_day()
    {
        (AuthSession target, _) = await PlayerAsync();
        (AuthSession other, _) = await PlayerAsync();
        (AuthSession reporter, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteLineAsync(target, "line one")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteLineAsync(other, "line two")).StatusCode);

        // A day's worth already sent - closed, so the one-open-report rule is not what stops the next.
        for (int i = 0; i < SignatureReportLimits.PerDay; i++)
        {
            await SqlAsync($"""
                INSERT INTO signature_reports (reporter_id, target_id, signature, reason, created_at, resolved_at, resolution)
                VALUES ({reporter.PlayerId}, {target.PlayerId}, {"old " + i}, 'Spam', now() - interval '1 hour', now(), 'Dismissed')
                """);
        }

        HttpResponseMessage refused = await ReportAsync(reporter, other.PlayerId);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(ErrorCodes.ReportLimitReached, await CodeAsync(refused));
    }

    [Fact]
    public async Task The_queue_gathers_a_lines_reports_and_dismissing_them_thanks_every_reporter()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession target, string targetName) = await PlayerAsync();
        (AuthSession first, _) = await PlayerAsync();
        (AuthSession second, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteLineAsync(target, "a perfectly fine line")).StatusCode);

        await ReportAsync(first, target.PlayerId, SignatureReportReason.Offensive, "rude?");
        await ReportAsync(second, target.PlayerId, SignatureReportReason.Other);

        AdminReportedPlayer reported = Assert.Single((await QueueAsync(admin)).Players, p => p.PlayerId == target.PlayerId);
        Assert.Equal((targetName, "a perfectly fine line"), (reported.DisplayName, reported.Signature));
        Assert.Equal([SignatureReportReason.Offensive, SignatureReportReason.Other], reported.Reports.Select(r => r.Reason));
        Assert.Equal(("rude?", "a perfectly fine line"), (reported.Reports[0].Note, reported.Reports[0].Signature));

        HttpResponseMessage dismissed = await Client.SendAsync(Send(HttpMethod.Post,
            $"/v1/admin/signatures/{target.PlayerId}/dismiss-reports", admin, new SignatureReportDismissRequest("Within the rules")));
        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);

        Assert.DoesNotContain((await QueueAsync(admin)).Players, p => p.PlayerId == target.PlayerId);

        foreach (AuthSession reporter in new[] { first, second })
        {
            MailItem thanks = Assert.Single((await BoxAsync(reporter)).Mails, m => m.Title == "Thank you for your report");
            Assert.Contains("found it within the rules", thanks.Body);
        }

        // Seen once is seen: the same line cannot be reported again by them - a changed one can.
        Assert.Equal(HttpStatusCode.Conflict, (await ReportAsync(first, target.PlayerId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteLineAsync(target, "a less fine line")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(first, target.PlayerId)).StatusCode);

        // Nothing open is nothing to dismiss.
        await Client.SendAsync(Send(HttpMethod.Post, $"/v1/admin/signatures/{target.PlayerId}/dismiss-reports", admin));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.SendAsync(Send(HttpMethod.Post,
            $"/v1/admin/signatures/{target.PlayerId}/dismiss-reports", admin))).StatusCode);
    }

    [Fact]
    public async Task Taking_the_line_down_closes_its_reports_and_thanks_the_reporters()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession target, _) = await PlayerAsync();
        (AuthSession reporter, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteLineAsync(target, "visit my shop")).StatusCode);
        await ReportAsync(reporter, target.PlayerId);

        HttpResponseMessage removed = await Client.SendAsync(Send(HttpMethod.Post,
            $"/v1/admin/signatures/{target.PlayerId}/remove", admin, new SignatureRemoveRequest("Advertising", 0, false, false)));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        Assert.DoesNotContain((await QueueAsync(admin)).Players, p => p.PlayerId == target.PlayerId);
        MailItem thanks = Assert.Single((await BoxAsync(reporter)).Mails, m => m.Title == "Thank you for your report");
        Assert.Contains("taken it down", thanks.Body);
    }

    [Fact]
    public async Task Only_administrators_see_the_queue()
    {
        (AuthSession player, _) = await PlayerAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/signatures/reports", player))).StatusCode);
    }
}
