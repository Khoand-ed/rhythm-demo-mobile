using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Players;

namespace Promuse.Api.Tests;

[Collection(ApiCollection.Name)]
public class PlayerEndpointTests(PromuseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    /// <summary>A signed-in guest, which is the cheapest way to get a player.</summary>
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

    private HttpRequestMessage Get(string url, AuthSession session, string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);

        if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        return request;
    }

    private HttpRequestMessage Put(string url, AuthSession session, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);

        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        return request;
    }

    private async Task<(PlayerState State, string ETag)> ReadAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(Get("/v1/players/me", session));

        response.EnsureSuccessStatusCode();

        return ((await response.Content.ReadFromJsonAsync<PlayerState>())!,
                response.Headers.ETag!.ToString());
    }

    // ----------------------------------------------------------------- read

    [Fact]
    public async Task A_new_player_starts_with_the_roster_and_bag_the_client_expects()
    {
        AuthSession session = await SignInAsync();

        (PlayerState state, string etag) = await ReadAsync(session);

        Assert.Equal(session.PlayerId, state.PlayerId);
        Assert.Equal(1, state.Level);

        // The four playable operators from PlayerData.Initialization.
        Assert.Equal(["AMIYA", "ECHO", "NOVA", "PULSE"], state.Characters.Select(c => c.CharacterId));

        // ItemStack(0, 5), ItemStack(1, 500), ItemStack(2, 1000).
        Assert.Equal([(0, 5), (1, 500), (2, 1000)],
            state.Inventory.Select(i => (i.ItemId, i.Amount)));

        // Always four slots, all empty to begin with.
        Assert.Equal(4, state.Squad.Count);
        Assert.All(state.Squad, Assert.Null);

        // GetMaxReason(1) is 82, and a new player starts full.
        Assert.Equal(82, state.Stamina.Max);
        Assert.Equal(82, state.Stamina.Current);
        Assert.Null(state.Stamina.NextPointAt);

        Assert.Equal("W/\"1\"", etag);
    }

    [Fact]
    public async Task Reading_twice_without_a_write_returns_the_same_version()
    {
        AuthSession session = await SignInAsync();

        (PlayerState first, string etagA) = await ReadAsync(session);
        (PlayerState second, string etagB) = await ReadAsync(session);

        Assert.Equal(etagA, etagB);
        Assert.Equal(first.StateVersion, second.StateVersion);

        // serverTime moves between the two, which is exactly why the ETag is weak.
        Assert.True(second.ServerTime >= first.ServerTime);
    }

    [Fact]
    public async Task A_matching_if_none_match_answers_304()
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await ReadAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Get("/v1/players/me", session, etag));

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task Reading_without_a_token_is_refused_in_the_standard_shape()
    {
        HttpResponseMessage response = await _client.GetAsync("/v1/players/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---------------------------------------------------------------- squad

    [Fact]
    public async Task Setting_the_squad_returns_the_new_state_and_bumps_the_version()
    {
        AuthSession session = await SignInAsync();
        (PlayerState before, string etag) = await ReadAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Put(
            "/v1/players/me/squad", session,
            new SquadRequest(["AMIYA", null, "NOVA", null]), etag));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = (await response.Content.ReadFromJsonAsync<PlayerState>())!;

        Assert.Equal(["AMIYA", null, "NOVA", null], after.Squad);
        Assert.Equal(before.StateVersion + 1, after.StateVersion);

        // The new ETag comes back on the write, so a client never has to re-read
        // just to learn the version it is now at.
        Assert.Equal($"W/\"{after.StateVersion}\"", response.Headers.ETag!.ToString());
    }

    /// <summary>
    /// The property the whole If-Match design exists for: two devices, one read
    /// each, and the second write must not silently win.
    /// </summary>
    [Fact]
    public async Task A_write_from_a_stale_read_is_refused_with_412()
    {
        AuthSession session = await SignInAsync();

        // Both "devices" read the same version.
        (_, string shared) = await ReadAsync(session);

        HttpResponseMessage phone = await _client.SendAsync(Put(
            "/v1/players/me/squad", session, new SquadRequest(["AMIYA", null, null, null]), shared));

        HttpResponseMessage tablet = await _client.SendAsync(Put(
            "/v1/players/me/squad", session, new SquadRequest(["NOVA", null, null, null]), shared));

        Assert.Equal(HttpStatusCode.OK, phone.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, tablet.StatusCode);

        var problem = (await tablet.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(ErrorCodes.StateConflict, problem.Code);

        // 第一个写入的赢 / And the first writer's value survived. A silent
        // last-write-wins would have left NOVA here.
        (PlayerState state, _) = await ReadAsync(session);

        Assert.Equal("AMIYA", state.Squad[0]);
    }

    [Fact]
    public async Task A_write_without_if_match_is_refused_with_428()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Put(
            "/v1/players/me/squad", session, new SquadRequest([null, null, null, null]), ifMatch: null));

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Equal(ErrorCodes.PreconditionRequired,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Fact]
    public async Task The_same_operator_cannot_hold_two_slots()
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await ReadAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Put(
            "/v1/players/me/squad", session,
            new SquadRequest(["AMIYA", "AMIYA", null, null]), etag));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = (await response.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(ErrorCodes.ValidationFailed, problem.Code);
        Assert.Contains("squad", problem.Errors!.Keys);
    }

    [Fact]
    public async Task An_operator_the_player_does_not_own_is_refused()
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await ReadAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Put(
            "/v1/players/me/squad", session,
            new SquadRequest(["SKADI", null, null, null]), etag));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task A_squad_that_is_not_four_entries_is_refused(int size)
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await ReadAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Put(
            "/v1/players/me/squad", session,
            new SquadRequest(new string?[size]), etag));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// A refused write must not consume the version, or a client that fixes its
    /// mistake would then have to re-read before retrying.
    /// </summary>
    [Fact]
    public async Task A_rejected_write_leaves_the_version_untouched()
    {
        AuthSession session = await SignInAsync();
        (PlayerState before, string etag) = await ReadAsync(session);

        await _client.SendAsync(Put("/v1/players/me/squad", session,
            new SquadRequest(["AMIYA", "AMIYA", null, null]), etag));

        (PlayerState after, string etagAfter) = await ReadAsync(session);

        Assert.Equal(before.StateVersion, after.StateVersion);
        Assert.Equal(etag, etagAfter);
    }

    // ----------------------------------------------------- desktop character

    [Fact]
    public async Task Setting_and_clearing_the_desktop_character()
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await ReadAsync(session);

        HttpResponseMessage set = await _client.SendAsync(Put(
            "/v1/players/me/desktop-character", session,
            new DesktopCharacterRequest("ECHO"), etag));

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var withCharacter = (await set.Content.ReadFromJsonAsync<PlayerState>())!;

        Assert.Equal("ECHO", withCharacter.DesktopCharacterId);

        HttpResponseMessage cleared = await _client.SendAsync(Put(
            "/v1/players/me/desktop-character", session,
            new DesktopCharacterRequest(null),
            $"W/\"{withCharacter.StateVersion}\""));

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<PlayerState>())!.DesktopCharacterId);
    }

    [Fact]
    public async Task An_unowned_desktop_character_is_refused()
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await ReadAsync(session);

        HttpResponseMessage response = await _client.SendAsync(Put(
            "/v1/players/me/desktop-character", session,
            new DesktopCharacterRequest("SKADI"), etag));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ------------------------------------------------------------ isolation

    /// <summary>
    /// The access token decides whose save this is. Nothing in the request names
    /// a player, so there is no parameter to tamper with - but this asserts that,
    /// rather than assuming it.
    /// </summary>
    [Fact]
    public async Task One_players_write_does_not_touch_another()
    {
        AuthSession a = await SignInAsync();
        AuthSession b = await SignInAsync();

        (_, string etagA) = await ReadAsync(a);

        await _client.SendAsync(Put("/v1/players/me/squad", a,
            new SquadRequest(["PULSE", null, null, null]), etagA));

        (PlayerState stateB, _) = await ReadAsync(b);

        Assert.NotEqual(a.PlayerId, b.PlayerId);
        Assert.All(stateB.Squad, Assert.Null);
    }
}
