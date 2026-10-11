using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Config;
using Promuse.Contracts.Gacha;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// Remote config end to end: reading it, changing it as an administrator, and the switches
/// actually biting.
///
/// 用完要还原 / Every test that changes the config puts the default back in a finally. The
/// config is global and the other test classes in the collection run against the same database
/// afterwards - a maintenance window left on here would fail every one of them.
/// </summary>
[Collection(ApiCollection.Name)]
public class RemoteConfigEndpointTests(PromuseApiFactory factory)
{
    private static readonly ConfigDocument Default = new(
        new MaintenanceWindow(false, null, null), "0.0.0", new FeatureFlags(true, true, true, true), null);

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
        return (await response.Content.ReadFromJsonAsync<AuthSession>())!;
    }

    /// <summary>
    /// A registered account made administrator the only way there is - by hand, in the database -
    /// then signed in again, as an operator would.
    /// </summary>
    private async Task<(AuthSession Session, string Username)> AdminAsync()
    {
        string username = "admin_" + Unique[..10];

        var register = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(username, "correct horse battery")),
        };
        register.Headers.Add("Idempotency-Key", Unique);
        (await _client.SendAsync(register)).EnsureSuccessStatusCode();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().Database
                .ExecuteSqlAsync($"UPDATE accounts SET is_admin = TRUE WHERE username = {username}");
        }

        HttpResponseMessage login = await _client.PostAsJsonAsync("/v1/auth/login",
            new LoginRequest(username, "correct horse battery"));
        login.EnsureSuccessStatusCode();

        return ((await login.Content.ReadFromJsonAsync<AuthSession>())!, username);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, AuthSession? session, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (session is not null) request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<RemoteConfig> LiveAsync()
    {
        HttpResponseMessage response = await _client.GetAsync("/v1/config");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RemoteConfig>())!;
    }

    private async Task<HttpResponseMessage> PutAsync(AuthSession admin, ConfigDocument document, string? note, int? ifMatch)
    {
        HttpRequestMessage request = Authed(HttpMethod.Put, "/v1/admin/config", admin, new ConfigUpdateRequest(document, note));
        if (ifMatch is { } v) request.Headers.TryAddWithoutValidation("If-Match", $"W/\"{v}\"");
        return await _client.SendAsync(request);
    }

    /// <summary>Saves a document on top of whatever is live.</summary>
    private async Task<RemoteConfig> SetAsync(AuthSession admin, ConfigDocument document, string? note = null)
    {
        HttpResponseMessage response = await PutAsync(admin, document, note, (await LiveAsync()).Version);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RemoteConfig>())!;
    }

    private Task RestoreAsync(AuthSession admin) => SetAsync(admin, Default, "test cleanup");

    private static async Task<ApiProblem> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>())!;

    // ---------------------------------------------------------------- reading

    [Fact]
    public async Task Anyone_can_read_the_config_and_an_unchanged_one_answers_304()
    {
        HttpResponseMessage first = await _client.GetAsync("/v1/config");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var config = (await first.Content.ReadFromJsonAsync<RemoteConfig>())!;
        Assert.True(config.Version >= 1);
        Assert.Equal($"W/\"{config.Version}\"", first.Headers.ETag!.ToString());

        var again = new HttpRequestMessage(HttpMethod.Get, "/v1/config");
        again.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(again)).StatusCode);
    }

    // ------------------------------------------------------------------ admin

    [Fact]
    public async Task The_admin_page_is_served_with_or_without_a_trailing_slash()
    {
        foreach (string path in new[] { "/admin", "/admin/" })
        {
            HttpResponseMessage page = await _client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
            Assert.Contains("Promuse LiveOps", await page.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task The_admin_api_refuses_strangers_and_players()
    {
        HttpResponseMessage anonymous = await _client.SendAsync(Authed(HttpMethod.Get, "/v1/admin/config/history", null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        AuthSession player = await GuestAsync();
        HttpResponseMessage forbidden = await _client.SendAsync(Authed(HttpMethod.Get, "/v1/admin/config/history", player));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, (await ProblemAsync(forbidden)).Code);

        HttpResponseMessage write = await PutAsync(player, Default, null, (await LiveAsync()).Version);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task A_save_needs_the_version_it_was_made_from()
    {
        (AuthSession admin, string username) = await AdminAsync();
        int live = (await LiveAsync()).Version;

        try
        {
            Assert.Equal(HttpStatusCode.PreconditionRequired, (await PutAsync(admin, Default, null, null)).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await PutAsync(admin, Default, null, live - 1)).StatusCode);

            HttpResponseMessage saved = await PutAsync(admin, Default with { Announcement = "Hello" }, "say hello", live);
            Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
            Assert.Equal(live + 1, (await saved.Content.ReadFromJsonAsync<RemoteConfig>())!.Version);

            // 两个管理员同时改 / The second admin, still holding the old version, is refused
            // rather than silently overwriting the first one's change.
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await PutAsync(admin, Default, null, live)).StatusCode);

            var history = (await (await _client.SendAsync(
                    Authed(HttpMethod.Get, "/v1/admin/config/history?limit=5", admin)))
                .Content.ReadFromJsonAsync<ConfigHistory>())!;

            ConfigRevision newest = history.Revisions[0];
            Assert.Equal(live + 1, newest.Version);
            Assert.Equal("Hello", newest.Document.Announcement);
            Assert.Equal(username, newest.CreatedBy);
            Assert.Equal("say hello", newest.Note);
        }
        finally
        {
            await RestoreAsync(admin);
        }
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.2.3.4")]
    [InlineData("")]
    public async Task A_minimum_version_that_does_not_parse_is_refused(string version)
    {
        (AuthSession admin, _) = await AdminAsync();

        HttpResponseMessage response = await PutAsync(admin, Default with { MinClientVersion = version }, null,
            (await LiveAsync()).Version);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_rollback_puts_an_old_version_back_as_a_new_one()
    {
        (AuthSession admin, _) = await AdminAsync();

        try
        {
            RemoteConfig first = await SetAsync(admin, Default with { Announcement = "first" });
            RemoteConfig second = await SetAsync(admin, Default with { Announcement = "second" });

            var request = Authed(HttpMethod.Post, "/v1/admin/config/rollback", admin, new ConfigRollbackRequest(first.Version, null));
            request.Headers.TryAddWithoutValidation("If-Match", $"W/\"{second.Version}\"");
            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            RemoteConfig live = await LiveAsync();
            Assert.Equal(second.Version + 1, live.Version);
            Assert.Equal("first", live.Document.Announcement);

            var history = (await (await _client.SendAsync(
                    Authed(HttpMethod.Get, "/v1/admin/config/history?limit=1", admin)))
                .Content.ReadFromJsonAsync<ConfigHistory>())!;
            Assert.Equal(first.Version, history.Revisions[0].RolledBackFrom);
        }
        finally
        {
            await RestoreAsync(admin);
        }
    }

    // ---------------------------------------------------------------- switches

    [Fact]
    public async Task Maintenance_refuses_players_and_lets_admins_config_and_sign_in_through()
    {
        (AuthSession admin, _) = await AdminAsync();
        AuthSession player = await GuestAsync();

        try
        {
            await SetAsync(admin, Default with
            {
                Maintenance = new MaintenanceWindow(true, "Patch 1.1 is being installed.", DateTimeOffset.UtcNow.AddHours(1)),
            });

            HttpResponseMessage refused = await _client.SendAsync(Authed(HttpMethod.Get, "/v1/players/me", player));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.NotNull(refused.Headers.RetryAfter);

            ApiProblem problem = await ProblemAsync(refused);
            Assert.Equal(ErrorCodes.Maintenance, problem.Code);
            Assert.Equal("Patch 1.1 is being installed.", problem.Detail);

            // 管理员要能进去看 / An administrator can still use the game to check it.
            Assert.Equal(HttpStatusCode.OK,
                (await _client.SendAsync(Authed(HttpMethod.Get, "/v1/players/me", admin))).StatusCode);

            // 客户端要能知道为什么 / The config is how a client learns why it is being refused.
            Assert.True((await LiveAsync()).Document.Maintenance.Enabled);

            // Signing in stays open - otherwise nobody could sign in to turn maintenance off.
            await GuestAsync();
        }
        finally
        {
            await RestoreAsync(admin);
        }

        Assert.Equal(HttpStatusCode.OK,
            (await _client.SendAsync(Authed(HttpMethod.Get, "/v1/players/me", player))).StatusCode);
    }

    [Fact]
    public async Task A_build_older_than_the_minimum_is_refused_with_426()
    {
        (AuthSession admin, _) = await AdminAsync();
        AuthSession player = await GuestAsync();

        HttpRequestMessage From(string? version)
        {
            HttpRequestMessage request = Authed(HttpMethod.Get, "/v1/players/me", player);
            if (version is not null) request.Headers.Add(ClientVersion.Header, version);
            return request;
        }

        try
        {
            await SetAsync(admin, Default with { MinClientVersion = "2.0" });

            HttpResponseMessage old = await _client.SendAsync(From("1.9.9"));
            Assert.Equal((HttpStatusCode)426, old.StatusCode);
            Assert.Equal(ErrorCodes.ClientOutdated, (await ProblemAsync(old)).Code);

            Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(From("2.0.0"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(From("2.1"))).StatusCode);

            // No header: a tool or a test, not a game build - let through.
            Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(From(null))).StatusCode);

            // An old build can still read the config, which is what tells it to update.
            var config = new HttpRequestMessage(HttpMethod.Get, "/v1/config");
            config.Headers.Add(ClientVersion.Header, "1.0");
            Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(config)).StatusCode);
        }
        finally
        {
            await RestoreAsync(admin);
        }
    }

    [Fact]
    public async Task A_switched_off_feature_refuses_only_its_own_endpoint()
    {
        (AuthSession admin, _) = await AdminAsync();
        AuthSession player = await GuestAsync();

        try
        {
            await SetAsync(admin, Default with { Features = new FeatureFlags(Gacha: false, Shop: true, Ranked: false, Leaderboards: true) });

            var pull = Authed(HttpMethod.Post, "/v1/gacha/pulls", player, new GachaPullRequest("standard", 1));
            pull.Headers.Add("Idempotency-Key", Unique);
            HttpResponseMessage pulled = await _client.SendAsync(pull);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, pulled.StatusCode);
            Assert.Equal(ErrorCodes.FeatureDisabled, (await ProblemAsync(pulled)).Code);

            // 只关拉取 / Only the pull is off: the banners can still be looked at.
            Assert.Equal(HttpStatusCode.OK,
                (await _client.SendAsync(Authed(HttpMethod.Get, "/v1/gacha/banners", player))).StatusCode);

            var start = Authed(HttpMethod.Post, "/v1/runs", player, new StartRunRequest(Played.Stage, Played.Operator));
            start.Headers.Add("Idempotency-Key", Unique);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.SendAsync(start)).StatusCode);

            // Nothing was charged for either refusal.
            var state = (await (await _client.SendAsync(Authed(HttpMethod.Get, "/v1/players/me", player)))
                .Content.ReadFromJsonAsync<Contracts.Players.PlayerState>())!;
            Assert.Equal(500, state.Inventory.Single(i => i.ItemId == 1).Amount);
            Assert.Equal(state.Stamina.Max, state.Stamina.Current);
        }
        finally
        {
            await RestoreAsync(admin);
        }
    }
}
