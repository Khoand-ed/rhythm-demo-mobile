using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.Runs;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Leaderboards;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;
using Promuse.Contracts.Runs;
using Promuse.Persistence;
using Promuse.Persistence.Entities;
using Promuse.Scoring.Review;

namespace Promuse.Api.Tests;

/// <summary>
/// A result from the device to the leaderboard: the review's three verdicts, what each one
/// does to the missions and the boards, and the boards themselves.
///
/// 每个测试自己的关卡视角 / The boards are shared by every test in the collection, so tests that
/// care about rank use their own fresh players and assert relative positions, never "first
/// place" on a board another test may also be writing to.
///
/// 分数是真的 / Every score here is what its trace really scores - the server replays it, and a
/// claim the trace does not back is flagged. A run's score is chosen by how many of the first
/// notes it leaves unplayed (Played.Win misses), and each test that ranks picks miss counts no
/// other test uses, so nothing else on the board lands between its runs.
/// </summary>
[Collection(ApiCollection.Name)]
public class ScoreEndpointTests(PromuseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Unique => Guid.NewGuid().ToString("N");

    // --------------------------------------------------------------- helpers

    private async Task<AuthSession> SignInAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/guest")
        {
            Content = JsonContent.Create(new GuestSignInRequest(Unique)),
        };
        request.Headers.Add("Idempotency-Key", Unique);

        HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthSession>(Json))!;
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession session,
                                           object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", key ?? Unique);
        return request;
    }

    private async Task<HttpResponseMessage> StartAsync(AuthSession session, string characterId = Played.Operator,
                                                       string stage = Played.Stage) =>
        await _client.SendAsync(Send(HttpMethod.Post, "/v1/runs", session, new StartRunRequest(stage, characterId)));

    /// <summary>Opens a run, ages it past the chart, and closes it with the request given.</summary>
    private async Task<HttpResponseMessage> PlayAsync(AuthSession session, CompleteRunRequest request,
                                                      bool age = true, string? key = null)
    {
        var ticket = (await (await StartAsync(session)).Content.ReadFromJsonAsync<RunTicket>(Json))!;

        if (age) await Played.AgeAsync(factory, ticket.RunId, TimeSpan.FromMinutes(5));

        return await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, request, key));
    }

    private async Task<RunCompletion> PlayWinAsync(AuthSession session, int misses = 0)
    {
        HttpResponseMessage response = await PlayAsync(session, new CompleteRunRequest(true, Played.Win(factory, misses)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;
    }

    private async Task<LeaderboardPage> BoardAsync(AuthSession session, LeaderboardPeriod period = LeaderboardPeriod.AllTime,
                                                   int limit = 100)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get,
            $"/v1/leaderboards/{Played.Stage}?period={period}&limit={limit}", session));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeaderboardPage>(Json))!;
    }

    private async Task<int> PlaySongProgressAsync(AuthSession session)
    {
        MissionBoards boards = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/missions", session)))
            .Content.ReadFromJsonAsync<MissionBoards>(Json))!;

        return boards.Weekly.Missions.Single(m => m.MissionId == "weekly.play5").Progress;
    }

    private async Task<RunScore> StoredScoreAsync(Guid accountId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        return await db.RunScores.AsNoTracking().SingleAsync(s => s.AccountId == accountId);
    }

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>(Json))!.Code;

    // ------------------------------------------------------------ the operator

    [Fact]
    public async Task A_run_must_name_its_operator()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await StartAsync(session, characterId: "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_run_with_an_operator_the_player_does_not_own_is_refused()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await StartAsync(session, characterId: "W");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.CharacterNotOwned, await CodeOf(response));
    }

    // ---------------------------------------------------------------- accepted

    [Fact]
    public async Task An_honest_win_is_accepted_ranked_and_kept_with_its_trace()
    {
        AuthSession session = await SignInAsync();

        RunResult played = Played.Win(factory);
        RunCompletion done = (await (await PlayAsync(session, new CompleteRunRequest(true, played)))
            .Content.ReadFromJsonAsync<RunCompletion>(Json))!;

        Assert.True(done.Won);
        Assert.Equal(ScoreVerdict.Accepted, done.Review!.Verdict);
        Assert.Empty(done.Review.Reasons);
        Assert.Equal(played.Score, done.Review.Score);
        Assert.True(done.Review.IsPersonalBest);
        Assert.NotNull(done.Review.AllTimeRank);
        Assert.NotNull(done.Review.WeeklyRank);

        Assert.Equal(1, await PlaySongProgressAsync(session));

        LeaderboardPage board = await BoardAsync(session);
        Assert.Equal("all", board.PeriodKey);
        Assert.Null(board.ResetsAt);
        Assert.Equal(played.Score, board.Me!.Score);
        Assert.Equal(Played.Operator, board.Me.CharacterId);
        Assert.Equal(done.Review.AllTimeRank, board.Me.Rank);

        // 证据都在 / The evidence is all on record: the gzipped trace as it arrived, and what
        // the review measured from it.
        RunScore stored = await StoredScoreAsync(session.PlayerId);
        Assert.Equal(ScoreVerdict.Accepted, stored.Verdict);
        Assert.NotNull(stored.Trace);
        Assert.True(stored.TraceFrames > 2000);
        Assert.Equal(179, stored.TimingSamples);
        Assert.NotNull(stored.Ceiling);

        // The server judged the trace itself, and came to what was sent.
        using JsonDocument replay = JsonDocument.Parse(stored.Replay!);
        Assert.Equal(played.Score, replay.RootElement.GetProperty("score").GetInt32());
        Assert.Equal(played.Perfect, replay.RootElement.GetProperty("perfect").GetInt32());
    }

    [Fact]
    public async Task The_weekly_board_is_keyed_by_week_and_says_when_it_resets()
    {
        AuthSession session = await SignInAsync();
        RunCompletion done = await PlayWinAsync(session);

        LeaderboardPage weekly = await BoardAsync(session, LeaderboardPeriod.Weekly);

        Assert.Matches(@"^\d{4}-W\d{2}$", weekly.PeriodKey);
        Assert.True(weekly.ResetsAt > weekly.ServerTime);
        Assert.Equal(done.Review!.Score, weekly.Me!.Score);
    }

    [Fact]
    public async Task Only_a_better_run_replaces_a_personal_best()
    {
        AuthSession session = await SignInAsync();

        // Fewer notes left unplayed is a higher score.
        RunCompletion middle = await PlayWinAsync(session, misses: 10);
        Assert.True(middle.Review!.IsPersonalBest);

        RunCompletion worse = await PlayWinAsync(session, misses: 20);
        Assert.False(worse.Review!.IsPersonalBest);
        Assert.True(worse.Review.Score < middle.Review.Score);
        Assert.Equal(middle.Review.Score, (await BoardAsync(session)).Me!.Score);

        RunCompletion best = await PlayWinAsync(session, misses: 0);
        Assert.True(best.Review!.IsPersonalBest);
        Assert.Equal(best.Review.Score, (await BoardAsync(session)).Me!.Score);
    }

    [Fact]
    public async Task Higher_scores_rank_above_and_a_tie_goes_to_whoever_set_it_first()
    {
        // 只有这里用 7 和 8 / Miss counts no other test plays, so nothing can land inside the trio.
        AuthSession first = await SignInAsync();
        AuthSession second = await SignInAsync();
        AuthSession third = await SignInAsync();

        await PlayWinAsync(first, misses: 8);
        await PlayWinAsync(second, misses: 8);
        await PlayWinAsync(third, misses: 7);

        // Relative order only: other tests write to this board too.
        var mine = new[] { first.PlayerId, second.PlayerId, third.PlayerId };
        List<LeaderboardEntry> ours = (await BoardAsync(first))
            .Entries.Where(e => mine.Contains(e.PlayerId)).ToList();

        Assert.Equal(new[] { third.PlayerId, first.PlayerId, second.PlayerId },
                     ours.Select(e => e.PlayerId).ToArray());
        Assert.Equal(ours[0].Rank + 1, ours[1].Rank);
        Assert.Equal(ours[1].Rank + 1, ours[2].Rank);
    }

    [Fact]
    public async Task The_callers_own_line_is_there_even_when_it_is_off_the_page()
    {
        AuthSession top = await SignInAsync();
        AuthSession me = await SignInAsync();

        await PlayWinAsync(top, misses: 0);
        await PlayWinAsync(me, misses: 25);

        LeaderboardPage board = await BoardAsync(me, limit: 1);

        Assert.Single(board.Entries);
        Assert.NotEqual(me.PlayerId, board.Entries[0].PlayerId);
        Assert.Equal(me.PlayerId, board.Me!.PlayerId);
        Assert.True(board.Me.Rank > 1);
    }

    [Fact]
    public async Task Every_line_carries_the_tag_that_tells_two_players_of_one_name_apart()
    {
        AuthSession session = await SignInAsync();
        await PlayWinAsync(session, misses: 2);

        PlayerState state = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", session)))
            .Content.ReadFromJsonAsync<PlayerState>(Json))!;

        // The caller's own line, which is built by a separate query from the page.
        LeaderboardPage board = await BoardAsync(session);
        Assert.Equal((state.DisplayName, state.Tag), (board.Me!.DisplayName, board.Me.Tag));

        // And a line on the page carries the tag its player's card shows.
        LeaderboardEntry top = board.Entries[0];
        PlayerProfile card = (await (await _client.SendAsync(Send(HttpMethod.Get, $"/v1/players/{top.PlayerId}/profile", session)))
            .Content.ReadFromJsonAsync<PlayerProfile>(Json))!;
        Assert.Equal((card.DisplayName, card.Tag), (top.DisplayName, top.Tag));
    }

    // ----------------------------------------------------------------- rejected

    [Fact]
    public async Task A_score_above_the_ceiling_is_rejected_and_counts_for_nothing()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PlayAsync(session,
            new CompleteRunRequest(true, Played.Win(factory) with { Score = 9_999_999 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var done = (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;

        Assert.False(done.Won);
        Assert.Equal(ScoreVerdict.Rejected, done.Review!.Verdict);
        Assert.Equal(new[] { ScoreReviewCodes.ScoreAboveCeiling }, done.Review.Reasons);
        Assert.Null(done.Review.AllTimeRank);

        // 不算通关 / Not a clear: the mission did not move, and nothing reached the board.
        Assert.Equal(0, await PlaySongProgressAsync(session));
        Assert.Null((await BoardAsync(session)).Me);

        // 但留了底 / But it is on record, rejected, with the bound it broke.
        RunScore stored = await StoredScoreAsync(session.PlayerId);
        Assert.Equal(ScoreVerdict.Rejected, stored.Verdict);
        Assert.True(stored.Score > stored.Ceiling);
    }

    [Fact]
    public async Task A_win_closed_straight_after_opening_is_rejected()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PlayAsync(session,
            new CompleteRunRequest(true, Played.Win(factory)), age: false);

        var done = (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;

        Assert.Equal(ScoreVerdict.Rejected, done.Review!.Verdict);
        Assert.Contains(ScoreReviewCodes.FinishedTooFast, done.Review.Reasons);
    }

    [Fact]
    public async Task A_trace_that_is_not_even_base64_is_rejected_not_a_server_error()
    {
        AuthSession session = await SignInAsync();
        RunResult garbled = Played.Win(factory) with { Trace = "this is not base64!" };

        HttpResponseMessage response = await PlayAsync(session, new CompleteRunRequest(true, garbled));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var done = (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;
        Assert.Equal(new[] { ScoreReviewCodes.TraceInvalid }, done.Review!.Reasons);
    }

    [Fact]
    public async Task Rules_the_server_does_not_hold_are_named_as_the_reason()
    {
        AuthSession session = await SignInAsync();
        RunResult skewed = Played.Win(factory) with { RulesetFingerprint = "0123456789abcdef" };

        var done = (await (await PlayAsync(session, new CompleteRunRequest(true, skewed)))
            .Content.ReadFromJsonAsync<RunCompletion>(Json))!;

        Assert.Equal(new[] { ScoreReviewCodes.RulesetMismatch }, done.Review!.Reasons);
    }

    // ------------------------------------------------------------------ flagged

    [Fact]
    public async Task A_scripted_looking_run_is_a_clear_but_stays_off_the_board_and_is_told_only_under_review()
    {
        AuthSession session = await SignInAsync();

        // 零抖动 / No spread at all: every press on the frame nearest its note.
        HttpResponseMessage response = await PlayAsync(session,
            new CompleteRunRequest(true, Played.Win(factory, jitterMs: 0)));

        var done = (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;

        Assert.True(done.Won);
        Assert.Equal(ScoreVerdict.Flagged, done.Review!.Verdict);
        Assert.Equal(new[] { ScoreReviewCodes.UnderReview }, done.Review.Reasons);
        Assert.Null(done.Review.AllTimeRank);

        Assert.Equal(1, await PlaySongProgressAsync(session));
        Assert.Null((await BoardAsync(session)).Me);

        // 哪个信号只在服务端 / Which signal fired is on the server and nowhere else.
        RunScore stored = await StoredScoreAsync(session.PlayerId);
        Assert.Equal(new[] { ReviewFlags.TimingFrameLocked }, stored.Flags);
    }

    [Fact]
    public async Task A_score_its_trace_does_not_make_is_a_clear_held_off_the_board_for_review()
    {
        AuthSession session = await SignInAsync();
        RunResult honest = Played.Win(factory);

        // Self-consistent and under the ceiling: only the replay can tell.
        HttpResponseMessage response = await PlayAsync(session,
            new CompleteRunRequest(true, honest with { Score = honest.Score + 777 }));

        var done = (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;

        // 按主人的选择: 标记, 不拒绝 / Flagged, not rejected, as the owner chose: the clear stands.
        Assert.True(done.Won);
        Assert.Equal(ScoreVerdict.Flagged, done.Review!.Verdict);
        Assert.Equal(new[] { ScoreReviewCodes.UnderReview }, done.Review.Reasons);
        Assert.Equal(1, await PlaySongProgressAsync(session));
        Assert.Null((await BoardAsync(session)).Me);

        RunScore stored = await StoredScoreAsync(session.PlayerId);
        Assert.Equal(new[] { ReviewFlags.ReplayMismatch }, stored.Flags);
        Assert.Equal(honest.Score + 777, stored.Score);

        using JsonDocument replay = JsonDocument.Parse(stored.Replay!);
        Assert.Equal(honest.Score, replay.RootElement.GetProperty("score").GetInt32());
    }

    // ----------------------------------------------------------- malformed

    [Fact]
    public async Task A_win_without_a_result_is_malformed()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PlayAsync(session, new CompleteRunRequest(true, null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Negative_counts_are_malformed_and_nothing_is_stored()
    {
        AuthSession session = await SignInAsync();
        RunResult negative = Played.Win(factory) with { Miss = -1 };

        HttpResponseMessage response = await PlayAsync(session, new CompleteRunRequest(true, negative));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        Assert.False(await db.RunScores.AnyAsync(s => s.AccountId == session.PlayerId));
    }

    [Fact]
    public async Task A_loss_may_close_without_a_result()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PlayAsync(session, new CompleteRunRequest(false, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var done = (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!;
        Assert.False(done.Won);
        Assert.Null(done.Review);
    }

    // ------------------------------------------------------- idempotency

    [Fact]
    public async Task A_replayed_completion_ranks_once_and_answers_the_same()
    {
        AuthSession session = await SignInAsync();
        var ticket = (await (await StartAsync(session)).Content.ReadFromJsonAsync<RunTicket>(Json))!;
        await Played.AgeAsync(factory, ticket.RunId, TimeSpan.FromMinutes(5));

        var request = new CompleteRunRequest(true, Played.Win(factory, misses: 4));
        string key = ticket.RunId.ToString();

        HttpResponseMessage first = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, request, key));
        HttpResponseMessage again = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, request, key));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Equal(1, await PlaySongProgressAsync(session));
    }

    // ---------------------------------------------------------- the board

    [Fact]
    public async Task An_unknown_stage_has_no_board_and_a_limit_out_of_range_is_refused()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage unknown = await _client.SendAsync(Send(HttpMethod.Get, "/v1/leaderboards/stage_nope", session));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(ErrorCodes.StageNotFound, await CodeOf(unknown));

        HttpResponseMessage zero = await _client.SendAsync(Send(HttpMethod.Get, $"/v1/leaderboards/{Played.Stage}?limit=0", session));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, zero.StatusCode);
    }

    [Fact]
    public async Task The_board_is_not_public()
    {
        HttpResponseMessage response = await _client.GetAsync($"/v1/leaderboards/{Played.Stage}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
