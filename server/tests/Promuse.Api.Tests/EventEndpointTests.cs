using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Events;
using Promuse.Contracts.Gacha;
using Promuse.Contracts.Runs;
using Promuse.Contracts.Shop;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// Events end to end: an administrator creates one, players check in, count missions, claim,
/// earn the currency on its stages and spend it in its shop.
///
/// 自己建活动 / Every test makes its own event through the admin API, with a unique id and its own
/// currency item, and switches it off afterwards - the seeded event's dates are real dates, and
/// a test leaning on it would start failing the day it ends.
/// </summary>
[Collection(ApiCollection.Name)]
public class EventEndpointTests(PromuseApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private const int Orundum = 1, Permit = 10;
    private const int Token = 777;

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    // ---------------------------------------------------------------- helpers

    private async Task<AuthSession> GuestAsync()
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

    private async Task<AuthSession> AdminAsync()
    {
        string username = "evadmin_" + Unique[..10];
        var register = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(username, "correct horse battery")),
        };
        register.Headers.Add("Idempotency-Key", Unique);
        (await _client.SendAsync(register)).EnsureSuccessStatusCode();

        await SqlAsync($"UPDATE accounts SET is_admin = TRUE WHERE username = {username}");

        HttpResponseMessage login = await _client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(username, "correct horse battery"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthSession>(Json))!;
    }

    private async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().Database.ExecuteSqlAsync(sql);
    }

    private Task GrantAsync(Guid accountId, int itemId, int amount) => SqlAsync($"""
        INSERT INTO player_items (account_id, item_id, amount) VALUES ({accountId}, {itemId}, {amount})
        ON CONFLICT (account_id, item_id) DO UPDATE SET amount = player_items.amount + EXCLUDED.amount
        """);

    private async Task<int> BalanceAsync(Guid accountId, int itemId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().PlayerItems
            .Where(i => i.AccountId == accountId && i.ItemId == itemId).Select(i => i.Amount).FirstOrDefaultAsync();
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession? session, object? body = null,
                                           string? key = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (session is not null) request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method == HttpMethod.Post) request.Headers.Add("Idempotency-Key", key ?? Unique);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    /// <summary>An event that is open right now, using every module, on chart stage_001.</summary>
    private static EventDefinition Open() => new(
        "Test event", "subtitle", "A test.",
        DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), true,
        Token, "standard",
        [new(1, new(Orundum, 100)), new(2, new(Orundum, 200))],
        [
            new("clear1", "Clear any song once", EventGoal.PlaySong, 1, 10, null, null),
            new("stage1", "Clear stage_001 once", EventGoal.ClearStage, 1, 10, Played.Stage, null),
            new("score", "Score 500", EventGoal.ReachScore, 1, 5, null, 500),
            new("pull10", "Pull 10", EventGoal.Headhunt, 10, 5, null, null),
            new("buy1", "Buy once", EventGoal.BuyShopItem, 1, 5, null, null),
        ],
        [new("m15", 15, new(Permit, 2)), new("m35", 35, new(Orundum, 500))],
        [new("permit", new(Permit, 1), 40, 2), new("orundum", new(Orundum, 10), 5, null)],
        [new(Played.Stage, 25)]);

    private async Task<HttpResponseMessage> PutEventAsync(AuthSession admin, string id, EventDefinition d, int? ifMatch = null) =>
        await _client.SendAsync(Send(HttpMethod.Put, $"/v1/admin/events/{id}", admin, new EventUpsertRequest(d),
            ifMatch: ifMatch is { } v ? $"W/\"{v}\"" : null));

    private async Task<(AuthSession Admin, string Id)> CreateAsync(EventDefinition? definition = null)
    {
        AuthSession admin = await AdminAsync();
        string id = "t_" + Unique[..12];
        HttpResponseMessage response = await PutEventAsync(admin, id, definition ?? Open());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (admin, id);
    }

    /// <summary>Switches the test's event off so it stops counting other tests' runs.</summary>
    private async Task CloseAsync(AuthSession admin, string id)
    {
        AdminEventList list = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/admin/events", admin)))
            .Content.ReadFromJsonAsync<AdminEventList>(Json))!;
        AdminEvent mine = list.Events.Single(e => e.EventId == id);
        (await PutEventAsync(admin, id, mine.Definition with { IsActive = false }, mine.Version)).EnsureSuccessStatusCode();
    }

    private async Task<EventView> ViewAsync(AuthSession player, string id) =>
        (await (await _client.SendAsync(Send(HttpMethod.Get, $"/v1/events/{id}", player)))
            .Content.ReadFromJsonAsync<EventView>(Json))!;

    private async Task<(HttpStatusCode Status, EventActionResult? Result, string? Code)> ActAsync(
        AuthSession player, string path, object? body = null, string? key = null)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post, path, player, body ?? new { }, key));
        if (response.StatusCode == HttpStatusCode.Created)
        {
            return (response.StatusCode, await response.Content.ReadFromJsonAsync<EventActionResult>(Json), null);
        }

        return (response.StatusCode, null, (await response.Content.ReadFromJsonAsync<ApiProblem>(Json))!.Code);
    }

    /// <summary>A ranked win on stage_001, closed as the score tests close one.</summary>
    private async Task<RunCompletion> WinAsync(AuthSession player, int score = 1000)
    {
        HttpResponseMessage start = await _client.SendAsync(Send(HttpMethod.Post, "/v1/runs", player,
            new StartRunRequest(Played.Stage, Played.Operator)));
        RunTicket ticket = (await start.Content.ReadFromJsonAsync<RunTicket>(Json))!;
        await Played.AgeAsync(factory, ticket.RunId, TimeSpan.FromMinutes(5));

        HttpResponseMessage done = await _client.SendAsync(Send(HttpMethod.Post, $"/v1/runs/{ticket.RunId}/complete", player,
            new CompleteRunRequest(true, Played.Win(factory, score))));
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        return (await done.Content.ReadFromJsonAsync<RunCompletion>(Json))!;
    }

    // ---------------------------------------------------------------- reading

    [Fact]
    public async Task A_new_player_sees_the_event_untouched()
    {
        (AuthSession admin, string id) = await CreateAsync();
        try
        {
            AuthSession player = await GuestAsync();

            EventList list = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/events", player)))
                .Content.ReadFromJsonAsync<EventList>(Json))!;
            Assert.Contains(list.Events, e => e.EventId == id && e.IsOpen);

            EventView view = await ViewAsync(player, id);
            Assert.True(view.IsOpen);
            Assert.Equal(0, view.CurrencyBalance);
            Assert.Equal(0, view.Points);
            Assert.Equal((0, true), (view.CheckIn!.Claimed, view.CheckIn.CanClaimToday));
            Assert.All(view.Missions, m => Assert.Equal(0, m.Progress));
            Assert.All(view.Milestones, m => Assert.False(m.IsClaimable));
            Assert.Equal(2, view.Shop.Single(o => o.Offer.OfferId == "permit").Remaining);
            Assert.Null(view.Shop.Single(o => o.Offer.OfferId == "orundum").Remaining);
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    [Fact]
    public async Task An_unknown_or_switched_off_event_is_404()
    {
        (AuthSession admin, string id) = await CreateAsync();
        AuthSession player = await GuestAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.SendAsync(Send(HttpMethod.Get, "/v1/events/no_such_event", player))).StatusCode);

        await CloseAsync(admin, id);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.SendAsync(Send(HttpMethod.Get, $"/v1/events/{id}", player))).StatusCode);
    }

    // ---------------------------------------------------------------- check-in

    [Fact]
    public async Task One_check_in_a_day_paying_the_days_in_order()
    {
        (AuthSession admin, string id) = await CreateAsync();
        try
        {
            AuthSession player = await GuestAsync();
            string key = Unique;

            var first = await ActAsync(player, $"/v1/events/{id}/check-in", key: key);
            Assert.Equal(HttpStatusCode.Created, first.Status);
            Assert.Equal((Orundum, 100), (first.Result!.Granted[0].ItemId, first.Result.Granted[0].Amount));
            Assert.Equal(600, await BalanceAsync(player.PlayerId, Orundum));
            Assert.False(first.Result.Event.CheckIn!.CanClaimToday);

            // The same press again is a replay, not a second day.
            Assert.Equal(HttpStatusCode.Created, (await ActAsync(player, $"/v1/events/{id}/check-in", key: key)).Status);

            var again = await ActAsync(player, $"/v1/events/{id}/check-in");
            Assert.Equal((HttpStatusCode.Conflict, ErrorCodes.NothingToClaim), (again.Status, again.Code));
            Assert.Equal(600, await BalanceAsync(player.PlayerId, Orundum));

            // 第二天 / Tomorrow: move the first check-in back a day and day 2 pays.
            await SqlAsync($"UPDATE event_check_ins SET period_key = '2000-01-01' WHERE account_id = {player.PlayerId} AND event_id = {id}");
            var tomorrow = await ActAsync(player, $"/v1/events/{id}/check-in");
            Assert.Equal(200, tomorrow.Result!.Granted[0].Amount);
            Assert.Equal(2, tomorrow.Result.Event.CheckIn!.Claimed);
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    // ---------------------------------------------------------- missions & stages

    [Fact]
    public async Task A_cleared_event_stage_counts_missions_and_pays_the_currency()
    {
        (AuthSession admin, string id) = await CreateAsync();
        try
        {
            AuthSession player = await GuestAsync();

            RunCompletion done = await WinAsync(player, score: 1000);
            Assert.Equal(25, done.Player.Inventory.Single(i => i.ItemId == Token).Amount);

            EventView view = await ViewAsync(player, id);
            Assert.Equal(25, view.CurrencyBalance);
            Assert.True(view.Missions.Single(m => m.Mission.MissionId == "clear1").IsComplete);
            Assert.True(view.Missions.Single(m => m.Mission.MissionId == "stage1").IsComplete);
            Assert.True(view.Missions.Single(m => m.Mission.MissionId == "score").IsComplete);

            // 先领任务拿分, 再领里程碑 / Missions pay points; points open milestones.
            var early = await ActAsync(player, $"/v1/events/{id}/milestones/m15/claim");
            Assert.Equal((HttpStatusCode.Conflict, ErrorCodes.NotEnoughPoints), (early.Status, early.Code));

            Assert.Equal(HttpStatusCode.Created, (await ActAsync(player, $"/v1/events/{id}/missions/clear1/claim")).Status);
            var second = await ActAsync(player, $"/v1/events/{id}/missions/stage1/claim");
            Assert.Equal(20, second.Result!.Event.Points);

            var twice = await ActAsync(player, $"/v1/events/{id}/missions/stage1/claim");
            Assert.Equal(ErrorCodes.AlreadyClaimed, twice.Code);

            var notYet = await ActAsync(player, $"/v1/events/{id}/missions/pull10/claim");
            Assert.Equal(ErrorCodes.MissionNotComplete, notYet.Code);

            var milestone = await ActAsync(player, $"/v1/events/{id}/milestones/m15/claim");
            Assert.Equal(HttpStatusCode.Created, milestone.Status);
            Assert.Equal(2, await BalanceAsync(player.PlayerId, Permit));
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    [Fact]
    public async Task Pulls_and_store_checkouts_count_while_the_event_is_open()
    {
        (AuthSession admin, string id) = await CreateAsync();
        try
        {
            AuthSession player = await GuestAsync();
            await GrantAsync(player.PlayerId, Permit, 10);
            await GrantAsync(player.PlayerId, 6, 240);

            var pull = Send(HttpMethod.Post, "/v1/gacha/pulls", player, new GachaPullRequest("standard", 10));
            Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(pull)).StatusCode);

            ShopCatalog catalog = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/shop/offers", player)))
                .Content.ReadFromJsonAsync<ShopCatalog>(Json))!;
            var buy = Send(HttpMethod.Post, "/v1/shop/purchases", player, new PurchaseRequest(catalog.Offers[0].OfferId, 1));
            Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(buy)).StatusCode);

            EventView view = await ViewAsync(player, id);
            Assert.Equal(10, view.Missions.Single(m => m.Mission.MissionId == "pull10").Progress);
            Assert.Equal(1, view.Missions.Single(m => m.Mission.MissionId == "buy1").Progress);
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    // ---------------------------------------------------------------- shop

    [Fact]
    public async Task The_event_shop_takes_the_currency_and_keeps_to_its_limit()
    {
        (AuthSession admin, string id) = await CreateAsync();
        try
        {
            AuthSession player = await GuestAsync();

            var broke = await ActAsync(player, $"/v1/events/{id}/shop/permit/purchase", new EventPurchaseRequest(1));
            Assert.Equal(ErrorCodes.InsufficientFunds, broke.Code);

            await GrantAsync(player.PlayerId, Token, 200);

            var bought = await ActAsync(player, $"/v1/events/{id}/shop/permit/purchase", new EventPurchaseRequest(2));
            Assert.Equal(HttpStatusCode.Created, bought.Status);
            Assert.Equal(120, bought.Result!.Event.CurrencyBalance);
            Assert.Equal(0, bought.Result.Event.Shop.Single(o => o.Offer.OfferId == "permit").Remaining);
            Assert.Equal(2, await BalanceAsync(player.PlayerId, Permit));

            // 限购 / The limit holds, and the refusal costs nothing.
            var over = await ActAsync(player, $"/v1/events/{id}/shop/permit/purchase", new EventPurchaseRequest(1));
            Assert.Equal(ErrorCodes.OutOfStock, over.Code);
            Assert.Equal(120, await BalanceAsync(player.PlayerId, Token));
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    [Fact]
    public async Task Nothing_can_be_done_in_an_event_that_has_not_started()
    {
        EventDefinition later = Open() with
        {
            StartsAt = DateTimeOffset.UtcNow.AddDays(3), EndsAt = DateTimeOffset.UtcNow.AddDays(10),
        };
        (AuthSession admin, string id) = await CreateAsync(later);
        try
        {
            AuthSession player = await GuestAsync();

            EventSummary listed = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/events", player)))
                .Content.ReadFromJsonAsync<EventList>(Json))!.Events.Single(e => e.EventId == id);
            Assert.False(listed.IsOpen);

            var early = await ActAsync(player, $"/v1/events/{id}/check-in");
            Assert.Equal((HttpStatusCode.Conflict, ErrorCodes.EventClosed), (early.Status, early.Code));

            // Clearing its stage before it starts pays nothing.
            RunCompletion done = await WinAsync(player);
            Assert.DoesNotContain(done.Player.Inventory, i => i.ItemId == Token);
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    // ---------------------------------------------------------------- admin

    [Fact]
    public async Task Only_admins_edit_events_and_every_save_needs_its_version()
    {
        AuthSession player = await GuestAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await PutEventAsync(player, "t_" + Unique[..10], Open())).StatusCode);

        (AuthSession admin, string id) = await CreateAsync();
        try
        {
            Assert.Equal(HttpStatusCode.PreconditionRequired, (await PutEventAsync(admin, id, Open())).StatusCode);

            HttpResponseMessage saved = await PutEventAsync(admin, id, Open() with { Title = "Renamed" }, 1);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(2, (await saved.Content.ReadFromJsonAsync<AdminEvent>(Json))!.Version);

            Assert.Equal(HttpStatusCode.PreconditionFailed, (await PutEventAsync(admin, id, Open(), 1)).StatusCode);

            HttpResponseMessage invalid = await PutEventAsync(admin, id,
                Open() with { Stages = [new("no_such_chart", 10)] }, 2);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

            EventCatalog catalog = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/admin/events/catalog", admin)))
                .Content.ReadFromJsonAsync<EventCatalog>(Json))!;
            Assert.Contains(Played.Stage, catalog.StageIds);
            Assert.Contains("standard", catalog.BannerIds);
        }
        finally
        {
            await CloseAsync(admin, id);
        }
    }

    [Fact]
    public async Task The_seeded_event_is_there_for_the_admin_page()
    {
        AuthSession admin = await AdminAsync();

        AdminEventList list = (await (await _client.SendAsync(Send(HttpMethod.Get, "/v1/admin/events", admin)))
            .Content.ReadFromJsonAsync<AdminEventList>(Json))!;

        AdminEvent dawn = list.Events.Single(e => e.EventId == SeedEvents.ResonantDawnId);
        Assert.Equal("event_amiya", dawn.Definition.BannerId);
        Assert.Equal(7, dawn.Definition.CheckIn.Count);
    }
}
