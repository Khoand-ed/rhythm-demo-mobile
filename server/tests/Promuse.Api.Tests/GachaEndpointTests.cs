using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Gacha;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// Headhunting against the real API and a real Postgres.
///
/// 不控制骰子 / The dice are the real ones. Every test here asserts something that holds for any
/// roll - what was charged, that each card became either an operator or certificates, that the
/// guarantee fires - and steers the outcome where it must by setting state in the database
/// (a pity count at the threshold, a roster that already owns everyone) rather than by faking
/// randomness the production path would never see.
/// </summary>
[Collection(ApiCollection.Name)]
public class GachaEndpointTests(PromuseApiFactory factory)
{
    private const int Orundum = 1;
    private const int Certificate = 8;
    private const int Permit = 10;

    private static readonly string[] Everyone = ["AMIYA", "ECHO", "NOVA", "PULSE"];

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    // ---------------------------------------------------------------- helpers

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

    private async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().Database.ExecuteSqlAsync(sql);
    }

    private Task GrantAsync(Guid accountId, int itemId, int amount) => SqlAsync($"""
        INSERT INTO player_items (account_id, item_id, amount)
        VALUES ({accountId}, {itemId}, {amount})
        ON CONFLICT (account_id, item_id)
        DO UPDATE SET amount = player_items.amount + EXCLUDED.amount
        """);

    private async Task OwnEveryoneAsync(Guid accountId)
    {
        foreach (string id in Everyone)
        {
            await SqlAsync($"""
                INSERT INTO player_characters (account_id, character_id, elite, level, exp, trust)
                VALUES ({accountId}, {id}, 0, 1, 0, 0)
                ON CONFLICT DO NOTHING
                """);
        }
    }

    private Task SetPityAsync(Guid accountId, string bannerId, int count) => SqlAsync($"""
        INSERT INTO gacha_pity (account_id, banner_id, pulls_since_five_star)
        VALUES ({accountId}, {bannerId}, {count})
        ON CONFLICT (account_id, banner_id) DO UPDATE SET pulls_since_five_star = EXCLUDED.pulls_since_five_star
        """);

    private async Task<int> BalanceAsync(Guid accountId, int itemId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        return await db.PlayerItems
            .Where(i => i.AccountId == accountId && i.ItemId == itemId)
            .Select(i => i.Amount)
            .FirstOrDefaultAsync();
    }

    private HttpRequestMessage Get(string url, AuthSession session)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        return request;
    }

    private Task<HttpResponseMessage> PullAsync(AuthSession session, string bannerId, int times, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/gacha/pulls")
        {
            Content = JsonContent.Create(new GachaPullRequest(bannerId, times)),
        };
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        request.Headers.Add("Idempotency-Key", key ?? Unique);

        return _client.SendAsync(request);
    }

    private async Task<GachaPullResult> PullOkAsync(AuthSession session, string bannerId, int times, string? key = null)
    {
        HttpResponseMessage response = await PullAsync(session, bannerId, times, key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GachaPullResult>())!;
    }

    private static async Task<ApiProblem> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>())!;

    // ---------------------------------------------------------------- banners

    [Fact]
    public async Task The_banners_are_served_with_their_rules_and_pool()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Get("/v1/gacha/banners", session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = (await response.Content.ReadFromJsonAsync<GachaBannerList>())!;

        // Other tests add banners of their own; the two seeded ones lead, in display order.
        Assert.Equal(["event_amiya", "standard"], list.Banners.Take(2).Select(b => b.BannerId));

        GachaBannerInfo standard = list.Banners[1];

        Assert.Equal((200, 1000, 8800), (standard.RateFiveStar, standard.RateFourStar, standard.RateThreeStar));
        Assert.Equal(30, standard.PityThreshold);
        Assert.Equal((Orundum, 180, 1800, (int?)Permit),
            (standard.CurrencyItemId, standard.CostSingle, standard.CostMulti, standard.TicketItemId));
        Assert.Null(standard.EndsAt);
        Assert.Equal(0, standard.PullsSinceFiveStar);

        // Rarest first, so the Details screen can print the pool top-down as it arrives.
        Assert.Equal([("AMIYA", 5), ("ECHO", 4), ("NOVA", 4), ("PULSE", 3)],
            standard.Pool.Select(p => (p.CharacterId, p.Rarity)));
        Assert.All(standard.Pool, p => Assert.Equal(Certificate, p.DuplicateItemId));
    }

    [Fact]
    public async Task An_ended_banner_is_not_listed_and_cannot_be_pulled()
    {
        AuthSession session = await SignInAsync();
        string id = "ended_" + Unique[..8];

        await SqlAsync($"""
            INSERT INTO gacha_banners (banner_id, starts_at, ends_at, rate_five_star, rate_four_star, rate_three_star,
                                       pity_threshold, currency_item_id, cost_single, cost_multi, ticket_item_id,
                                       is_active, sort_order)
            VALUES ({id}, {DateTimeOffset.UtcNow.AddDays(-10)}, {DateTimeOffset.UtcNow.AddDays(-1)},
                    200, 1000, 8800, 30, 1, 180, 1800, NULL, TRUE, 90)
            """);

        var list = (await (await _client.SendAsync(Get("/v1/gacha/banners", session)))
            .Content.ReadFromJsonAsync<GachaBannerList>())!;

        Assert.DoesNotContain(list.Banners, b => b.BannerId == id);

        HttpResponseMessage response = await PullAsync(session, id, 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.BannerClosed, (await ProblemAsync(response)).Code);
    }

    [Fact]
    public async Task An_unknown_banner_is_404()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PullAsync(session, "no_such_banner", 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.BannerNotFound, (await ProblemAsync(response)).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(11)]
    public async Task Only_one_or_ten_pulls_at_a_time(int times)
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PullAsync(session, "standard", times);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(500, await BalanceAsync(session.PlayerId, Orundum));
    }

    // ---------------------------------------------------------------- paying

    [Fact]
    public async Task A_single_pull_charges_180_orundum_and_every_card_is_an_operator_or_certificates()
    {
        AuthSession session = await SignInAsync();

        GachaPullResult result = await PullOkAsync(session, "standard", 1);

        Assert.Equal((Orundum, 180), (result.Charged.ItemId, result.Charged.Amount));
        Assert.Equal(320, await BalanceAsync(session.PlayerId, Orundum));

        GachaPullOutcome card = Assert.Single(result.Pulls);
        Assert.Contains(card.CharacterId, Everyone);

        // 新的进名册, 旧的变凭证 / New goes on the roster; a copy becomes certificates instead.
        Assert.Contains(result.Player.Characters, c => c.CharacterId == card.CharacterId);
        if (card.IsNew) Assert.Null(card.Converted);
        else Assert.Equal(Certificate, card.Converted!.ItemId);
    }

    [Fact]
    public async Task Permits_are_spent_before_orundum()
    {
        AuthSession session = await SignInAsync();
        await GrantAsync(session.PlayerId, Permit, 12);

        GachaPullResult result = await PullOkAsync(session, "standard", 10);

        Assert.Equal((Permit, 10), (result.Charged.ItemId, result.Charged.Amount));
        Assert.Equal(2, await BalanceAsync(session.PlayerId, Permit));
        Assert.Equal(500, await BalanceAsync(session.PlayerId, Orundum));
        Assert.Equal(10, result.Pulls.Count);
    }

    [Fact]
    public async Task Too_few_permits_for_the_whole_request_pays_in_orundum_instead()
    {
        AuthSession session = await SignInAsync();
        await GrantAsync(session.PlayerId, Permit, 3);
        await GrantAsync(session.PlayerId, Orundum, 1800);

        GachaPullResult result = await PullOkAsync(session, "standard", 10);

        Assert.Equal((Orundum, 1800), (result.Charged.ItemId, result.Charged.Amount));
        Assert.Equal(3, await BalanceAsync(session.PlayerId, Permit));
        Assert.Equal(500, await BalanceAsync(session.PlayerId, Orundum));
    }

    [Fact]
    public async Task A_pull_the_player_cannot_afford_changes_nothing()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await PullAsync(session, "standard", 10);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientFunds, (await ProblemAsync(response)).Code);
        Assert.Equal(500, await BalanceAsync(session.PlayerId, Orundum));

        var history = (await (await _client.SendAsync(Get("/v1/gacha/history", session)))
            .Content.ReadFromJsonAsync<GachaHistoryPage>())!;

        Assert.Empty(history.Entries);
    }

    // ---------------------------------------------------------------- results

    [Fact]
    public async Task Every_copy_already_owned_becomes_its_certificates()
    {
        AuthSession session = await SignInAsync();
        await OwnEveryoneAsync(session.PlayerId);
        await GrantAsync(session.PlayerId, Permit, 10);

        GachaPullResult result = await PullOkAsync(session, "standard", 10);

        Assert.All(result.Pulls, p =>
        {
            Assert.False(p.IsNew);
            Assert.Equal(Certificate, p.Converted!.ItemId);
        });

        // 5★ 20, 4★ 5, 3★ 1 - whatever the ten were, the bag holds exactly their sum.
        int expected = result.Pulls.Sum(p => p.Converted!.Amount);
        Assert.Equal(expected, await BalanceAsync(session.PlayerId, Certificate));
        Assert.Equal(4, result.Player.Characters.Count);
    }

    [Fact]
    public async Task At_the_threshold_the_next_pull_is_the_five_star_and_the_count_restarts()
    {
        AuthSession session = await SignInAsync();
        await SetPityAsync(session.PlayerId, "standard", 30);

        GachaPullResult result = await PullOkAsync(session, "standard", 1);

        GachaPullOutcome card = Assert.Single(result.Pulls);
        Assert.Equal(("AMIYA", 5), (card.CharacterId, card.Rarity));
        Assert.True(card.Guaranteed);
        Assert.True(card.IsNew);
        Assert.Equal(0, result.PullsSinceFiveStar);

        // 每个卡池分开数 / Each banner keeps its own count: the event banner never moved.
        var list = (await (await _client.SendAsync(Get("/v1/gacha/banners", session)))
            .Content.ReadFromJsonAsync<GachaBannerList>())!;

        Assert.Equal(0, list.Banners.Single(b => b.BannerId == "event_amiya").PullsSinceFiveStar);
    }

    [Fact]
    public async Task Two_pulls_racing_at_the_threshold_give_the_guarantee_once()
    {
        AuthSession session = await SignInAsync();
        await SetPityAsync(session.PlayerId, "standard", 30);
        await GrantAsync(session.PlayerId, Permit, 2);

        // 两个同时到 / Both read the count at once without the row lock, and both would pay out.
        GachaPullResult[] both = await Task.WhenAll(
            PullOkAsync(session, "standard", 1),
            PullOkAsync(session, "standard", 1));

        Assert.Equal(1, both.Count(r => r.Pulls[0].Guaranteed));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_replays_the_result_without_charging_again()
    {
        AuthSession session = await SignInAsync();
        string key = Unique;

        GachaPullResult first = await PullOkAsync(session, "standard", 1, key);
        GachaPullResult again = await PullOkAsync(session, "standard", 1, key);

        // 同一个结果 / The same cards, not a fresh roll - otherwise retrying would be a free reroll.
        Assert.Equal(first.BatchId, again.BatchId);
        Assert.Equal(first.Pulls[0].CharacterId, again.Pulls[0].CharacterId);
        Assert.Equal(320, await BalanceAsync(session.PlayerId, Orundum));
    }

    [Fact]
    public async Task Retries_with_one_key_racing_each_other_still_charge_once()
    {
        AuthSession session = await SignInAsync();
        await GrantAsync(session.PlayerId, Orundum, 1800);
        string key = Unique;

        // 同时到 / All five at once, which is what a client firing its retry before the first
        // answer arrives looks like. Each one reading "no record yet" and running the pull would
        // charge five times and roll five times.
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => PullAsync(session, "standard", 1, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        GachaPullResult[] results = await Task.WhenAll(
            responses.Select(async r => (await r.Content.ReadFromJsonAsync<GachaPullResult>())!));

        Assert.Single(results.Select(r => r.BatchId).Distinct());
        Assert.Equal(2300 - 180, await BalanceAsync(session.PlayerId, Orundum));
    }

    // ---------------------------------------------------------------- history

    [Fact]
    public async Task History_pages_newest_first_down_to_the_first_pull()
    {
        AuthSession session = await SignInAsync();
        await GrantAsync(session.PlayerId, Permit, 20);

        GachaPullResult older = await PullOkAsync(session, "standard", 10);
        GachaPullResult newer = await PullOkAsync(session, "event_amiya", 10);

        var first = (await (await _client.SendAsync(Get("/v1/gacha/history?limit=10", session)))
            .Content.ReadFromJsonAsync<GachaHistoryPage>())!;

        Assert.Equal(10, first.Entries.Count);
        Assert.All(first.Entries, e => Assert.Equal(newer.BatchId, e.BatchId));
        Assert.NotNull(first.NextBefore);

        var second = (await (await _client.SendAsync(
                Get($"/v1/gacha/history?limit=10&before={first.NextBefore}", session)))
            .Content.ReadFromJsonAsync<GachaHistoryPage>())!;

        Assert.Equal(10, second.Entries.Count);
        Assert.All(second.Entries, e => Assert.Equal(older.BatchId, e.BatchId));
        Assert.Null(second.NextBefore);

        long[] sequence = [.. first.Entries.Concat(second.Entries).Select(e => e.Sequence)];
        Assert.Equal(sequence.OrderByDescending(s => s), sequence);
    }

    [Fact]
    public async Task History_only_shows_the_callers_own_pulls()
    {
        AuthSession mine = await SignInAsync();
        AuthSession theirs = await SignInAsync();

        await PullOkAsync(theirs, "standard", 1);

        var page = (await (await _client.SendAsync(Get("/v1/gacha/history", mine)))
            .Content.ReadFromJsonAsync<GachaHistoryPage>())!;

        Assert.Empty(page.Entries);
    }
}
