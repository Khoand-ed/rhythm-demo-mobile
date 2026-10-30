using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthEndpointTests(PromuseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    private async Task<HttpResponseMessage> PostAsync(
        string url, object body, string? idempotencyKey = null, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };

        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);

        return await _client.SendAsync(request);
    }

    private async Task<AuthSession> GuestAsync(string? deviceId = null)
    {
        HttpResponseMessage response = await PostAsync(
            "/v1/auth/guest", new GuestSignInRequest(deviceId ?? Unique), Unique);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthSession>())!;
    }

    // ---------------------------------------------------------------- guest

    [Fact]
    public async Task Guest_sign_in_creates_a_player_then_reopens_the_same_one()
    {
        string device = Unique;

        HttpResponseMessage first = await PostAsync("/v1/auth/guest", new GuestSignInRequest(device), Unique);
        HttpResponseMessage second = await PostAsync("/v1/auth/guest", new GuestSignInRequest(device), Unique);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // 200 rather than 201: a reinstall sends a new idempotency key for a
        // device the server already knows, and must not get a second player.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var a = (await first.Content.ReadFromJsonAsync<AuthSession>())!;
        var b = (await second.Content.ReadFromJsonAsync<AuthSession>())!;

        Assert.Equal(a.PlayerId, b.PlayerId);
        Assert.True(a.IsGuest);
        Assert.NotEqual(a.Tokens.RefreshToken, b.Tokens.RefreshToken);
    }

    // --------------------------------------------------------- idempotency

    /// <summary>
    /// The case the header exists for: the response is lost, the client retries.
    /// The handler must not run twice, and the caller must get the first answer
    /// byte for byte.
    /// </summary>
    [Fact]
    public async Task Replaying_an_idempotency_key_returns_the_first_response_and_creates_nothing_new()
    {
        string key = Unique;
        var body = new RegisterRequest($"user_{Unique[..12]}", "correct horse battery");

        HttpResponseMessage first = await PostAsync("/v1/auth/register", body, key);
        HttpResponseMessage replay = await PostAsync("/v1/auth/register", body, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);

        var a = (await first.Content.ReadFromJsonAsync<AuthSession>())!;
        var b = (await replay.Content.ReadFromJsonAsync<AuthSession>())!;

        // Same player, and the same tokens - a replay returns the stored
        // response rather than running the handler and minting new ones.
        Assert.Equal(a.PlayerId, b.PlayerId);
        Assert.Equal(a.Tokens.RefreshToken, b.Tokens.RefreshToken);

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        Assert.Equal(1, await db.Accounts.CountAsync(x => x.Username == body.Username));
    }

    /// <summary>
    /// 二十四小时以后 / The contract keeps a key for 24 hours and no longer. A sign-up's stored
    /// answer is a session - live tokens for the account - so a day-old copy must not be handed to
    /// whoever presents the key next: past its 24 hours the same request is a new sign-up, which by
    /// then finds its name taken.
    /// </summary>
    [Fact]
    public async Task A_sign_up_retried_after_its_key_expired_is_a_new_request_not_a_replay()
    {
        string key = Unique;
        var body = new RegisterRequest($"late_{Unique[..12]}", "correct horse battery");

        HttpResponseMessage first = await PostAsync("/v1/auth/register", body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // A day and a second later.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
            DateTimeOffset expired = DateTimeOffset.UtcNow.AddSeconds(-1);

            await db.IdempotencyRecords
                .Where(r => r.Key == key)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CreatedAt, expired.AddHours(-24))
                    .SetProperty(r => r.ExpiresAt, expired));
        }

        HttpResponseMessage late = await PostAsync("/v1/auth/register", body, key);

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(ErrorCodes.UsernameTaken, (await late.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    /// <summary>
    /// Concurrency, not sequence. Both requests read no record and both proceed;
    /// the unique index is what stops the second from committing a second
    /// account. This is the property a lookup-then-insert cannot provide.
    /// </summary>
    [Fact]
    public async Task Concurrent_requests_with_one_idempotency_key_create_exactly_one_account()
    {
        string key = Unique;
        var body = new RegisterRequest($"race_{Unique[..12]}", "correct horse battery");

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => PostAsync("/v1/auth/register", body, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        Assert.Equal(1, await db.Accounts.CountAsync(x => x.Username == body.Username));
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_with_a_different_body_is_refused()
    {
        string key = Unique;

        await PostAsync("/v1/auth/register", new RegisterRequest($"one_{Unique[..12]}", "correct horse battery"), key);

        HttpResponseMessage second = await PostAsync(
            "/v1/auth/register", new RegisterRequest($"two_{Unique[..12]}", "correct horse battery"), key);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = (await second.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(ErrorCodes.IdempotencyKeyConflict, problem.Code);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_refused()
    {
        HttpResponseMessage response = await PostAsync("/v1/auth/guest", new GuestSignInRequest(Unique));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------- register

    [Fact]
    public async Task Duplicate_username_is_refused()
    {
        var body = new RegisterRequest($"dup_{Unique[..12]}", "correct horse battery");

        await PostAsync("/v1/auth/register", body, Unique);

        HttpResponseMessage second = await PostAsync("/v1/auth/register", body, Unique);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.UsernameTaken,
            (await second.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    // 一台设备多个账号 / One phone, several accounts: the game sends its device id with every sign-up,
    // and the device id is unique (a guest sign-in on a device reopens that device's account). A
    // second sign-up from the same device used to collide on it - and was answered USERNAME_TAKEN,
    // whatever the username.
    [Fact]
    public async Task A_second_account_can_be_registered_on_the_same_device()
    {
        string device = Unique;

        HttpResponseMessage first = await PostAsync("/v1/auth/register",
            new RegisterRequest($"dev1_{Unique[..12]}", "correct horse battery", device), Unique);
        HttpResponseMessage second = await PostAsync("/v1/auth/register",
            new RegisterRequest($"dev2_{Unique[..12]}", "correct horse battery", device), Unique);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task The_device_stays_with_the_account_it_was_first_linked_to()
    {
        string device = Unique;
        AuthSession guest = await GuestAsync(device);

        HttpResponseMessage registered = await PostAsync("/v1/auth/register",
            new RegisterRequest($"dev3_{Unique[..12]}", "correct horse battery", device), Unique);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // A guest sign-in on that device still reopens the guest, not the account made after it.
        AuthSession again = await GuestAsync(device);
        Assert.Equal(guest.PlayerId, again.PlayerId);
    }

    [Fact]
    public async Task A_taken_username_is_still_refused_from_a_linked_device()
    {
        string device = Unique;
        string name = $"dev4_{Unique[..12]}";

        await PostAsync("/v1/auth/register", new RegisterRequest(name, "correct horse battery", device), Unique);
        HttpResponseMessage second = await PostAsync("/v1/auth/register",
            new RegisterRequest(name, "correct horse battery", device), Unique);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.UsernameTaken, (await second.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Theory]
    [InlineData("ab", "correct horse battery")]        // username too short
    [InlineData("has spaces", "correct horse battery")] // username shape
    [InlineData("valid_name", "short")]                 // password too short
    public async Task Malformed_registration_is_422_with_the_offending_fields(string username, string password)
    {
        HttpResponseMessage response = await PostAsync(
            "/v1/auth/register", new RegisterRequest(username, password), Unique);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = (await response.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(ErrorCodes.ValidationFailed, problem.Code);
        Assert.NotNull(problem.Errors);
        Assert.NotEmpty(problem.Errors!);
    }

    // ---------------------------------------------------------------- login

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        var body = new RegisterRequest($"login_{Unique[..12]}", "correct horse battery");
        await PostAsync("/v1/auth/register", body, Unique);

        HttpResponseMessage response = await PostAsync(
            "/v1/auth/login", new LoginRequest(body.Username, body.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = (await response.Content.ReadFromJsonAsync<AuthSession>())!;

        Assert.False(session.IsGuest);
    }

    /// <summary>
    /// An unknown username and a wrong password must be indistinguishable, or
    /// the endpoint becomes a way to enumerate accounts.
    /// </summary>
    [Fact]
    public async Task Wrong_password_and_unknown_username_answer_identically()
    {
        var body = new RegisterRequest($"enum_{Unique[..12]}", "correct horse battery");
        await PostAsync("/v1/auth/register", body, Unique);

        HttpResponseMessage wrongPassword = await PostAsync(
            "/v1/auth/login", new LoginRequest(body.Username, "not the password"));

        HttpResponseMessage unknownUser = await PostAsync(
            "/v1/auth/login", new LoginRequest($"ghost_{Unique[..12]}", "not the password"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);

        var a = (await wrongPassword.Content.ReadFromJsonAsync<ApiProblem>())!;
        var b = (await unknownUser.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(a.Code, b.Code);
        Assert.Equal(a.Title, b.Title);
        Assert.Equal(a.Detail, b.Detail);
    }

    // -------------------------------------------------------------- refresh

    [Fact]
    public async Task Refresh_rotates_and_kills_the_token_it_replaced()
    {
        AuthSession session = await GuestAsync();

        HttpResponseMessage rotated = await PostAsync(
            "/v1/auth/refresh", new RefreshRequest(session.Tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        var pair = (await rotated.Content.ReadFromJsonAsync<TokenPair>())!;

        Assert.NotEqual(session.Tokens.RefreshToken, pair.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
    }

    /// <summary>
    /// The security property worth having. Presenting a rotated-away token means
    /// two parties hold it and the server cannot tell which is the thief, so the
    /// whole family dies and both must sign in again.
    /// </summary>
    [Fact]
    public async Task Replaying_a_rotated_refresh_token_revokes_the_whole_family()
    {
        AuthSession session = await GuestAsync();
        string stolen = session.Tokens.RefreshToken;

        HttpResponseMessage rotated = await PostAsync("/v1/auth/refresh", new RefreshRequest(stolen));
        var live = (await rotated.Content.ReadFromJsonAsync<TokenPair>())!;

        // The thief presents the copy they kept.
        HttpResponseMessage reuse = await PostAsync("/v1/auth/refresh", new RefreshRequest(stolen));

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(ErrorCodes.RefreshTokenReused,
            (await reuse.Content.ReadFromJsonAsync<ApiProblem>())!.Code);

        // 受害者也被登出 / And the legitimate holder's fresh token is dead too.
        // That is the point: the server cannot tell them apart, so it ends the
        // session rather than guessing.
        HttpResponseMessage victim = await PostAsync("/v1/auth/refresh", new RefreshRequest(live.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, victim.StatusCode);
    }

    [Fact]
    public async Task Unknown_refresh_token_is_refused_without_claiming_theft()
    {
        HttpResponseMessage response = await PostAsync(
            "/v1/auth/refresh", new RefreshRequest("not-a-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.RefreshTokenInvalid,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    // --------------------------------------------------------------- logout

    [Fact]
    public async Task Logout_ends_the_session_and_is_safe_to_repeat()
    {
        AuthSession session = await GuestAsync();

        HttpResponseMessage first = await PostAsync(
            "/v1/auth/logout", new RefreshRequest(session.Tokens.RefreshToken));

        HttpResponseMessage again = await PostAsync(
            "/v1/auth/logout", new RefreshRequest(session.Tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        HttpResponseMessage refresh = await PostAsync(
            "/v1/auth/refresh", new RefreshRequest(session.Tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logging_out_one_device_leaves_the_other_signed_in()
    {
        string device = Unique;

        // Two sign-ins from one account are two families.
        AuthSession phone = await GuestAsync(device);
        AuthSession tablet = await GuestAsync(device);

        await PostAsync("/v1/auth/logout", new RefreshRequest(phone.Tokens.RefreshToken));

        HttpResponseMessage stillGood = await PostAsync(
            "/v1/auth/refresh", new RefreshRequest(tablet.Tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, stillGood.StatusCode);
    }

    // ----------------------------------------------------------------- link

    [Fact]
    public async Task Linking_keeps_the_player_and_stops_it_being_a_guest()
    {
        AuthSession guest = await GuestAsync();
        var credentials = new RegisterRequest($"link_{Unique[..12]}", "correct horse battery");

        HttpResponseMessage response = await PostAsync(
            "/v1/auth/link", credentials, Unique, guest.Tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var linked = (await response.Content.ReadFromJsonAsync<AuthSession>())!;

        // 进度不能丢 / The player id survives. Losing it here is exactly why
        // players refuse to sign up at all.
        Assert.Equal(guest.PlayerId, linked.PlayerId);
        Assert.False(linked.IsGuest);

        HttpResponseMessage login = await PostAsync(
            "/v1/auth/login", new LoginRequest(credentials.Username, credentials.Password));

        Assert.Equal(guest.PlayerId,
            (await login.Content.ReadFromJsonAsync<AuthSession>())!.PlayerId);
    }

    [Fact]
    public async Task Linking_twice_is_refused()
    {
        AuthSession guest = await GuestAsync();

        await PostAsync("/v1/auth/link",
            new RegisterRequest($"once_{Unique[..12]}", "correct horse battery"),
            Unique, guest.Tokens.AccessToken);

        HttpResponseMessage second = await PostAsync("/v1/auth/link",
            new RegisterRequest($"twice_{Unique[..12]}", "correct horse battery"),
            Unique, guest.Tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.NotAGuest,
            (await second.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    /// <summary>
    /// An unauthenticated call must answer in the same problem+json shape as
    /// everything else, not the framework's empty 401 body.
    /// </summary>
    [Fact]
    public async Task Link_without_a_token_answers_the_standard_problem_shape()
    {
        HttpResponseMessage response = await PostAsync(
            "/v1/auth/link", new RegisterRequest($"anon_{Unique[..12]}", "correct horse battery"), Unique);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = (await response.Content.ReadFromJsonAsync<ApiProblem>())!;

        Assert.Equal(ErrorCodes.Unauthorized, problem.Code);
    }
}
