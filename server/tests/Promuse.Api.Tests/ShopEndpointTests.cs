using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Players;
using Promuse.Contracts.Shop;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

// 两层同名 / The row and the wire shape share a name, which is fine and normal -
// they are different layers. This file is one of only two places both are in
// scope, so it says which one it means rather than either being renamed.
using ShopOffer = Promuse.Contracts.Shop.ShopOffer;

namespace Promuse.Api.Tests;

[Collection(ApiCollection.Name)]
public class ShopEndpointTests(PromuseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    /// <summary>
    /// 新玩家买不起任何东西 / A new player owns items 0, 1 and 2 while every offer
    /// is priced in item 6, so nothing in the shop is affordable out of the box.
    /// That is how the game already was - the seeded inventory and the catalogue
    /// were written apart - and it is worth knowing rather than discovering. The
    /// tests grant currency directly so they can test the shop rather than that.
    /// </summary>
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

    private HttpRequestMessage Post(string url, AuthSession session, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };

        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        request.Headers.Add("Idempotency-Key", key ?? Unique);

        return request;
    }

    private async Task<ShopOffer> FirstOfferAsync(AuthSession session)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/shop/offers");
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);

        HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ShopCatalog>())!.Offers[0];
    }

    private async Task<int> BalanceAsync(Guid accountId, int itemId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        return await db.PlayerItems
            .Where(i => i.AccountId == accountId && i.ItemId == itemId)
            .Select(i => i.Amount)
            .FirstOrDefaultAsync();
    }

    // -------------------------------------------------------------- catalogue

    [Fact]
    public async Task The_catalogue_is_served_from_the_database()
    {
        AuthSession session = await SignInAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/shop/offers");
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var catalog = (await response.Content.ReadFromJsonAsync<ShopCatalog>())!;

        // The six offers seeded from the client's ShopItemDataList.
        Assert.Equal(6, catalog.Offers.Count);
        Assert.All(catalog.Offers, o => Assert.Equal(6, o.PriceItemId));

        // 顺序要稳定 / Stable between visits. Without an explicit ORDER BY the row
        // order is whatever the planner felt like, and the shop reshuffles itself
        // every time the screen opens.
        HttpResponseMessage again = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/v1/shop/offers")
            {
                Headers = { Authorization = new("Bearer", session.Tokens.AccessToken) },
            });

        var second = (await again.Content.ReadFromJsonAsync<ShopCatalog>())!;

        Assert.Equal(catalog.Offers.Select(o => o.OfferId), second.Offers.Select(o => o.OfferId));
    }

    // --------------------------------------------------------------- purchase

    [Fact]
    public async Task A_purchase_debits_the_price_and_credits_the_item()
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount * 3);

        HttpResponseMessage response = await _client.SendAsync(
            Post("/v1/shop/purchases", session, new PurchaseRequest(offer.OfferId, 2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<PurchaseResult>())!;

        Assert.Equal(offer.SellAmount * 2, result.Granted.Amount);
        Assert.Equal(offer.PriceAmount * 2, result.Charged.Amount);

        // One unit's worth of currency left, and the goods arrived.
        Assert.Equal(offer.PriceAmount, await BalanceAsync(session.PlayerId, offer.PriceItemId));
        Assert.Equal(offer.SellAmount * 2, await BalanceAsync(session.PlayerId, offer.SellItemId));

        // 回整个状态 / The whole player comes back, so the client never has to
        // work out what its inventory became.
        Assert.Contains(result.Player.Inventory, i => i.ItemId == offer.SellItemId);
    }

    /// <summary>
    /// The interaction between "a stack is deleted at zero" and the
    /// `amount > 0` CHECK: spending the last of something must remove the row,
    /// not update it to zero, which the constraint would refuse.
    /// </summary>
    [Fact]
    public async Task Spending_a_stack_to_exactly_nothing_removes_it()
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount);

        HttpResponseMessage response = await _client.SendAsync(
            Post("/v1/shop/purchases", session, new PurchaseRequest(offer.OfferId, 1)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, await BalanceAsync(session.PlayerId, offer.PriceItemId));

        var result = (await response.Content.ReadFromJsonAsync<PurchaseResult>())!;

        // Gone from the bag entirely rather than sitting there as "x0".
        Assert.DoesNotContain(result.Player.Inventory, i => i.ItemId == offer.PriceItemId);
    }

    [Fact]
    public async Task A_purchase_that_cannot_be_afforded_changes_nothing()
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount - 1);

        HttpResponseMessage response = await _client.SendAsync(
            Post("/v1/shop/purchases", session, new PurchaseRequest(offer.OfferId, 1)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientFunds,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);

        // 一分不少 / Untouched. A failed purchase that still took the money is
        // the worst outcome available here.
        Assert.Equal(offer.PriceAmount - 1, await BalanceAsync(session.PlayerId, offer.PriceItemId));
        Assert.Equal(0, await BalanceAsync(session.PlayerId, offer.SellItemId));
    }

    /// <summary>
    /// 并发不能透支 / Ten checkouts at once against a balance that affords three.
    /// Exactly three may succeed, and the balance may not go negative - which the
    /// CHECK would refuse anyway, turning an overdraw into a 500 rather than a
    /// silent one. A read-then-write could not promise this.
    /// </summary>
    [Fact]
    public async Task Concurrent_purchases_cannot_overdraw()
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount * 3);

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _client.SendAsync(
                Post("/v1/shop/purchases", session, new PurchaseRequest(offer.OfferId, 1)))));

        int created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        int refused = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(3, created);
        Assert.Equal(7, refused);

        Assert.Equal(0, await BalanceAsync(session.PlayerId, offer.PriceItemId));
        Assert.Equal(offer.SellAmount * 3, await BalanceAsync(session.PlayerId, offer.SellItemId));
    }

    [Fact]
    public async Task Replaying_a_purchase_key_charges_once()
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount * 2);

        string key = Unique;
        var body = new PurchaseRequest(offer.OfferId, 1);

        HttpResponseMessage first = await _client.SendAsync(Post("/v1/shop/purchases", session, body, key));
        HttpResponseMessage replay = await _client.SendAsync(Post("/v1/shop/purchases", session, body, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);

        var a = (await first.Content.ReadFromJsonAsync<PurchaseResult>())!;
        var b = (await replay.Content.ReadFromJsonAsync<PurchaseResult>())!;

        Assert.Equal(a.PurchaseId, b.PurchaseId);

        // Charged once, so one unit's worth of currency survives.
        Assert.Equal(offer.PriceAmount, await BalanceAsync(session.PlayerId, offer.PriceItemId));

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        Assert.Equal(1, await db.Purchases.CountAsync(p => p.AccountId == session.PlayerId));
    }

    [Fact]
    public async Task An_unknown_offer_is_refused()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(
            Post("/v1/shop/purchases", session, new PurchaseRequest(Guid.NewGuid(), 1)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.OfferNotFound,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task A_quantity_outside_the_allowed_range_is_refused(int quantity)
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        HttpResponseMessage response = await _client.SendAsync(
            Post("/v1/shop/purchases", session, new PurchaseRequest(offer.OfferId, quantity)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_purchase_bumps_the_state_version_so_a_held_etag_stops_matching()
    {
        AuthSession session = await SignInAsync();
        ShopOffer offer = await FirstOfferAsync(session);

        await GrantAsync(session.PlayerId, offer.PriceItemId, offer.PriceAmount);

        var read = new HttpRequestMessage(HttpMethod.Get, "/v1/players/me");
        read.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        var before = (await (await _client.SendAsync(read)).Content.ReadFromJsonAsync<PlayerState>())!;

        HttpResponseMessage response = await _client.SendAsync(
            Post("/v1/shop/purchases", session, new PurchaseRequest(offer.OfferId, 1)));

        var result = (await response.Content.ReadFromJsonAsync<PurchaseResult>())!;

        Assert.True(result.Player.StateVersion > before.StateVersion);
    }

    [Fact]
    public async Task Buying_without_a_token_is_refused()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/v1/shop/purchases", new PurchaseRequest(Guid.NewGuid(), 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
