using System.Net;
using System.Net.Http.Json;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Players;

namespace Promuse.Api.Tests;

/// <summary>
/// 起名 / Signing up gives a username, which signs in and is shown to no one, and no name. The
/// name screen sets the name once and the server draws its tag.
/// </summary>
[Collection(ApiCollection.Name)]
public class PlayerNamingEndpointTests(PromuseApiFactory factory)
{
    private const string Password = "correct horse battery";

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    private async Task<(AuthSession Session, string Username)> SignUpAsync()
    {
        string username = "namer_" + Unique[..12];
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(username, Password)),
        };
        request.Headers.Add("Idempotency-Key", Unique);

        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return ((await response.Content.ReadFromJsonAsync<AuthSession>())!, username);
    }

    private async Task<AuthSession> GuestAsync()
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

    private static HttpRequestMessage Send(
        HttpMethod method, string url, AuthSession session, object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);

        if (body is not null) request.Content = JsonContent.Create(body);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (method == HttpMethod.Post) request.Headers.Add("Idempotency-Key", Unique);

        return request;
    }

    private async Task<(PlayerState State, string ETag)> ReadAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", session));
        response.EnsureSuccessStatusCode();

        return ((await response.Content.ReadFromJsonAsync<PlayerState>())!, response.Headers.ETag!.ToString());
    }

    /// <summary>The name screen's call: the state just read, and the name typed.</summary>
    private async Task<HttpResponseMessage> NameAsync(AuthSession session, string name)
    {
        (_, string etag) = await ReadAsync(session);

        return await _client.SendAsync(
            Send(HttpMethod.Put, "/v1/players/me/name", session, new DisplayNameRequest(name), etag));
    }

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code;

    /// <summary>A name the rules allow and no other test uses: a letter, then seven hex digits.</summary>
    private static string FreshName() => "N" + Unique[..7];

    // ----------------------------------------------------------- signing up

    [Fact]
    public async Task Signing_up_leaves_the_player_unnamed()
    {
        (AuthSession session, _) = await SignUpAsync();

        (PlayerState state, _) = await ReadAsync(session);

        Assert.Null(state.DisplayName);
        Assert.Null(state.Tag);
    }

    [Fact]
    public async Task A_guest_is_still_named_at_once()
    {
        (PlayerState state, _) = await ReadAsync(await GuestAsync());

        Assert.StartsWith("Doctor", state.DisplayName);
        Assert.InRange(state.Tag!.Value, 0, 9999);
    }

    // ------------------------------------------------------------ the name

    [Fact]
    public async Task The_name_screen_sets_the_name_and_the_server_draws_its_tag()
    {
        (AuthSession session, string username) = await SignUpAsync();
        string name = FreshName();

        HttpResponseMessage response = await NameAsync(session, name);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlayerState named = (await response.Content.ReadFromJsonAsync<PlayerState>())!;

        Assert.Equal(name, named.DisplayName);
        Assert.NotEqual(username, named.DisplayName);
        Assert.InRange(named.Tag!.Value, 0, 9999);
        Assert.Equal(2, named.StateVersion);
        Assert.Equal("W/\"2\"", response.Headers.ETag!.ToString());

        (PlayerState read, _) = await ReadAsync(session);
        Assert.Equal((name, named.Tag), (read.DisplayName, read.Tag));
    }

    [Fact]
    public async Task A_name_is_chosen_once_and_never_changed()
    {
        (AuthSession session, _) = await SignUpAsync();
        string name = FreshName();

        (await NameAsync(session, name)).EnsureSuccessStatusCode();
        (PlayerState before, _) = await ReadAsync(session);

        HttpResponseMessage again = await NameAsync(session, FreshName());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.NameAlreadySet, await CodeOf(again));

        (PlayerState after, _) = await ReadAsync(session);
        Assert.Equal((name, before.Tag, before.StateVersion), (after.DisplayName, after.Tag, after.StateVersion));
    }

    [Fact]
    public async Task A_guest_has_a_name_already()
    {
        HttpResponseMessage response = await NameAsync(await GuestAsync(), FreshName());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.NameAlreadySet, await CodeOf(response));
    }

    [Theory]
    [InlineData("E")]
    [InlineData("Doctor123")]
    [InlineData("Ed d")]
    [InlineData("Ed_d")]
    [InlineData("Đức")]
    [InlineData("")]
    public async Task A_name_breaking_the_rules_is_refused_and_the_player_stays_unnamed(string name)
    {
        (AuthSession session, _) = await SignUpAsync();

        HttpResponseMessage response = await NameAsync(session, name);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeOf(response));

        (PlayerState state, _) = await ReadAsync(session);
        Assert.Null(state.DisplayName);
    }

    [Fact]
    public async Task Naming_needs_the_version_it_was_read_at()
    {
        (AuthSession session, _) = await SignUpAsync();

        HttpResponseMessage response = await _client.SendAsync(
            Send(HttpMethod.Put, "/v1/players/me/name", session, new DisplayNameRequest(FreshName())));

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task Players_who_choose_the_same_name_are_told_apart_by_the_tag()
    {
        string name = FreshName();
        var tags = new List<int>();

        for (int i = 0; i < 4; i++)
        {
            (AuthSession session, _) = await SignUpAsync();
            HttpResponseMessage response = await NameAsync(session, name);
            response.EnsureSuccessStatusCode();

            PlayerState state = (await response.Content.ReadFromJsonAsync<PlayerState>())!;
            Assert.Equal(name, state.DisplayName);
            tags.Add(state.Tag!.Value);
        }

        Assert.Equal(tags.Count, tags.Distinct().Count());
    }
}
