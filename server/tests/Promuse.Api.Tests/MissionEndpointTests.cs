using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Runs;
using Promuse.Contracts.Shop;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

[Collection(ApiCollection.Name)]
public class MissionEndpointTests(PromuseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>
    /// 和服务端同一套 / The API serialises enums as their names, so a reader using
    /// System.Text.Json defaults cannot parse `"tab": "Daily"` at all. The real
    /// client is unaffected - Json.NET maps strings to enums out of the box - but
    /// any .NET consumer needs this, and saying so here is cheaper than the
    /// exception it otherwise throws.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Unique => Guid.NewGuid().ToString("N");

    private async Task<AuthSession> SignInAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/guest")
        {
            Content = JsonContent.Create(new GuestSignInRequest(Unique)),
        };
        request.Headers.Add("Idempotency-Key", Unique);

        HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthSession>())!;
    }

    private HttpRequestMessage Send(HttpMethod method, string url, AuthSession session,
        object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (key is not null || method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", key ?? Unique);

        return request;
    }

    private async Task<MissionBoards> BoardsAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(
            Send(HttpMethod.Get, "/v1/missions", session));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<MissionBoards>(Json))!;
    }

    /// <summary>Opens a run and closes it, which is what a cleared song is.</summary>
    private async Task<HttpResponseMessage> PlayAsync(AuthSession session, bool won = true)
    {
        var ticket = (await (await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/runs", session, new StartRunRequest("stage_001"))))
            .Content.ReadFromJsonAsync<RunTicket>())!;

        return await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, new CompleteRunRequest(won)));
    }

    private async Task GrantAsync(Guid accountId, int itemId, int amount)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO player_items (account_id, item_id, amount)
            VALUES ({accountId}, {itemId}, {amount})
            ON CONFLICT (account_id, item_id)
            DO UPDATE SET amount = player_items.amount + EXCLUDED.amount
            """);
    }

    private static MissionState Mission(MissionBoard board, string id) =>
        board.Missions.Single(m => m.MissionId == id);

    // ----------------------------------------------------------------- read

    [Fact]
    public async Task A_new_player_sees_two_empty_boards()
    {
        AuthSession session = await SignInAsync();

        MissionBoards boards = await BoardsAsync(session);

        Assert.Equal(MissionTab.Daily, boards.Daily.Tab);
        Assert.Equal(MissionTab.Weekly, boards.Weekly.Tab);
        Assert.Equal(3, boards.Daily.Missions.Count);
        Assert.Equal(3, boards.Weekly.Missions.Count);
        Assert.Equal(3, boards.Daily.Rewards.Count);

        // 开板子不给东西 / Opening a board grants nothing: no progress, no points,
        // nothing claimable.
        Assert.All(boards.Daily.Missions, m => Assert.Equal(0, m.Progress));
        Assert.All(boards.Daily.Missions, m => Assert.False(m.IsClaimed));
        Assert.Equal(0, boards.Daily.Points);
        Assert.All(boards.Daily.Rewards, r => Assert.False(r.IsClaimable));

        Assert.True(boards.Daily.ResetsAt > boards.ServerTime);
        Assert.True(boards.Weekly.ResetsAt > boards.Daily.ResetsAt);
    }

    // ------------------------------------------------------------- counting

    /// <summary>
    /// 两个板子一起加 / One purchase advances the daily and the weekly counter at
    /// the same time. A caller never chooses a board.
    /// </summary>
    [Fact]
    public async Task A_purchase_advances_both_boards()
    {
        AuthSession session = await SignInAsync();

        var offer = (await (await _client.SendAsync(
            Send(HttpMethod.Get, "/v1/shop/offers", session)))
            .Content.ReadFromJsonAsync<ShopCatalog>())!.Offers[0];

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount);

        await _client.SendAsync(Send(HttpMethod.Post, "/v1/shop/purchases", session,
            new PurchaseRequest(offer.OfferId, 1)));

        MissionBoards boards = await BoardsAsync(session);

        Assert.Equal(1, Mission(boards.Daily, "daily.buy1").Progress);
        Assert.True(Mission(boards.Daily, "daily.buy1").IsComplete);
        Assert.Equal(1, Mission(boards.Weekly, "weekly.buy3").Progress);
        Assert.False(Mission(boards.Weekly, "weekly.buy3").IsComplete);
    }

    [Fact]
    public async Task Clearing_a_song_advances_the_play_missions()
    {
        AuthSession session = await SignInAsync();

        await PlayAsync(session, won: true);

        MissionBoards boards = await BoardsAsync(session);

        Assert.Equal(1, Mission(boards.Daily, "daily.play1").Progress);
        Assert.Equal(1, Mission(boards.Weekly, "weekly.play5").Progress);
    }

    /// <summary>
    /// 通关才算 / A run that was lost is not a clear, which is what GameManager
    /// already did. The stamina is spent either way - it was spent on opening.
    /// </summary>
    [Fact]
    public async Task Losing_a_run_does_not_advance_anything()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PlayAsync(session, won: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MissionBoards boards = await BoardsAsync(session);

        Assert.Equal(0, Mission(boards.Daily, "daily.play1").Progress);
    }

    [Fact]
    public async Task A_progress_bar_never_overfills()
    {
        AuthSession session = await SignInAsync();

        // Three clears against a mission that wanted one.
        for (int i = 0; i < 3; i++) await PlayAsync(session);

        MissionBoards boards = await BoardsAsync(session);

        Assert.Equal(1, Mission(boards.Daily, "daily.play1").Progress);
        Assert.Equal(2, Mission(boards.Daily, "daily.play2").Progress);
        Assert.Equal(3, Mission(boards.Weekly, "weekly.play5").Progress);
    }

    // --------------------------------------------------------------- claims

    [Fact]
    public async Task Claiming_a_finished_mission_pays_its_points()
    {
        AuthSession session = await SignInAsync();
        await PlayAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post,
            "/v1/missions/claims", session, new ClaimMissionRequest(MissionTab.Daily, "daily.play1")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<ClaimResult>(Json))!;

        // 任务给分不给物品 / A mission claim pays points, not items.
        Assert.Empty(result.Granted);
        Assert.Equal(1, result.Boards.Daily.Points);
        Assert.True(Mission(result.Boards.Daily, "daily.play1").IsClaimed);

        // And one point is what the first reward threshold wanted.
        Assert.True(result.Boards.Daily.Rewards.Single(r => r.RewardId == "daily.r1").IsClaimable);
    }

    [Fact]
    public async Task Claiming_an_unfinished_mission_is_refused()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post,
            "/v1/missions/claims", session, new ClaimMissionRequest(MissionTab.Daily, "daily.play1")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.MissionNotComplete,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Fact]
    public async Task Claiming_the_same_mission_twice_is_refused()
    {
        AuthSession session = await SignInAsync();
        await PlayAsync(session);

        var body = new ClaimMissionRequest(MissionTab.Daily, "daily.play1");

        await _client.SendAsync(Send(HttpMethod.Post, "/v1/missions/claims", session, body));

        // A different key, so this is a genuine second claim rather than a retry.
        HttpResponseMessage second = await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/missions/claims", session, body));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.AlreadyClaimed,
            (await second.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Fact]
    public async Task A_reward_below_its_threshold_is_refused()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post,
            "/v1/missions/reward-claims", session, new ClaimRewardRequest(MissionTab.Daily, "daily.r1")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.NotEnoughPoints,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Fact]
    public async Task Claiming_a_reward_hands_over_the_items()
    {
        AuthSession session = await SignInAsync();
        await PlayAsync(session);

        await _client.SendAsync(Send(HttpMethod.Post, "/v1/missions/claims", session,
            new ClaimMissionRequest(MissionTab.Daily, "daily.play1")));

        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post,
            "/v1/missions/reward-claims", session, new ClaimRewardRequest(MissionTab.Daily, "daily.r1")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<ClaimResult>(Json))!;

        // daily.r1 is 500 of item 2, which a new player already holds 1000 of.
        Assert.Equal(new[] { (2, 500) }, result.Granted.Select(g => (g.ItemId, g.Amount)));
        Assert.Equal(1500, result.Player.Inventory.Single(i => i.ItemId == 2).Amount);

        // The bag changed, so a held ETag must stop matching.
        Assert.True(result.Player.StateVersion > 1);
    }

    [Fact]
    public async Task Replaying_a_claim_key_pays_once()
    {
        AuthSession session = await SignInAsync();
        await PlayAsync(session);

        await _client.SendAsync(Send(HttpMethod.Post, "/v1/missions/claims", session,
            new ClaimMissionRequest(MissionTab.Daily, "daily.play1")));

        string key = Unique;
        var body = new ClaimRewardRequest(MissionTab.Daily, "daily.r1");

        HttpResponseMessage first = await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/missions/reward-claims", session, body, key));
        HttpResponseMessage replay = await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/missions/reward-claims", session, body, key));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var a = (await first.Content.ReadFromJsonAsync<ClaimResult>(Json))!;
        var b = (await replay.Content.ReadFromJsonAsync<ClaimResult>(Json))!;

        // Byte-identical, which only holds because the filter serialises with
        // the application's own settings - enums as strings included.
        Assert.Equal(a.Player.StateVersion, b.Player.StateVersion);
        Assert.Equal(1500, b.Player.Inventory.Single(i => i.ItemId == 2).Amount);
    }

    // ------------------------------------------------------------ run close

    [Fact]
    public async Task A_run_cannot_be_closed_twice()
    {
        AuthSession session = await SignInAsync();

        var ticket = (await (await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/runs", session, new StartRunRequest("stage_001"))))
            .Content.ReadFromJsonAsync<RunTicket>())!;

        await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, new CompleteRunRequest(true)));

        HttpResponseMessage second = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, new CompleteRunRequest(true)));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.RunNotOpen,
            (await second.Content.ReadFromJsonAsync<ApiProblem>())!.Code);

        // 只算一次 / And the mission counted once, not twice.
        MissionBoards boards = await BoardsAsync(session);
        Assert.Equal(1, Mission(boards.Weekly, "weekly.play5").Progress);
    }

    /// <summary>
    /// 幂等记录不能跨账号 / A stored response is replayed in full, and these
    /// responses carry player state, so the record has to belong to somebody. The
    /// client closes a run under the run's own id as the key - a value that is
    /// also in the URL - which makes "nobody else knows the key" the wrong thing
    /// to depend on.
    /// </summary>
    [Fact]
    public async Task One_players_idempotency_key_is_not_anothers()
    {
        AuthSession owner = await SignInAsync();
        AuthSession stranger = await SignInAsync();

        var ticket = (await (await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/runs", owner, new StartRunRequest("stage_001"))))
            .Content.ReadFromJsonAsync<RunTicket>())!;

        string key = ticket.RunId.ToString();

        HttpResponseMessage closed = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", owner, new CompleteRunRequest(true), key));

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        // 同一个键, 同一条路径, 换个人 / The same key on the same path from another
        // account. It must reach the handler, which refuses it because the run is
        // not theirs - not be answered out of the owner's record.
        HttpResponseMessage replayed = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", stranger, new CompleteRunRequest(true), key));

        Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
        Assert.Equal(ErrorCodes.RunNotOpen,
            (await replayed.Content.ReadFromJsonAsync<ApiProblem>())!.Code);

        // 而本人重试还是要拿到原来的答案 / And the owner's own retry still replays,
        // which is what the header is for in the first place.
        HttpResponseMessage retry = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", owner, new CompleteRunRequest(true), key));

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        // 还是只算一次 / Once, despite three requests naming the same run.
        MissionBoards boards = await BoardsAsync(owner);
        Assert.Equal(1, Mission(boards.Weekly, "weekly.play5").Progress);
    }

    /// <summary>
    /// 别人的局和不存在的局同样回答 / Someone else's run is answered exactly like a
    /// run that does not exist. Telling them apart would let a stranger probe
    /// which run ids are real.
    /// </summary>
    [Fact]
    public async Task Closing_someone_elses_run_is_refused_like_an_unknown_one()
    {
        AuthSession owner = await SignInAsync();
        AuthSession stranger = await SignInAsync();

        var ticket = (await (await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/runs", owner, new StartRunRequest("stage_001"))))
            .Content.ReadFromJsonAsync<RunTicket>())!;

        HttpResponseMessage theirs = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", stranger, new CompleteRunRequest(true)));

        HttpResponseMessage nonsense = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{Guid.NewGuid()}/complete", stranger, new CompleteRunRequest(true)));

        Assert.Equal(theirs.StatusCode, nonsense.StatusCode);

        var a = (await theirs.Content.ReadFromJsonAsync<ApiProblem>())!;
        var b = (await nonsense.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(a.Code, b.Code);
        Assert.Equal(a.Detail, b.Detail);
    }

    [Fact]
    public async Task One_players_progress_does_not_touch_another()
    {
        AuthSession a = await SignInAsync();
        AuthSession b = await SignInAsync();

        await PlayAsync(a);

        Assert.Equal(1, Mission((await BoardsAsync(a)).Daily, "daily.play1").Progress);
        Assert.Equal(0, Mission((await BoardsAsync(b)).Daily, "daily.play1").Progress);
    }

    [Fact]
    public async Task Reading_the_boards_without_a_token_is_refused()
    {
        HttpResponseMessage response = await _client.GetAsync("/v1/missions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
