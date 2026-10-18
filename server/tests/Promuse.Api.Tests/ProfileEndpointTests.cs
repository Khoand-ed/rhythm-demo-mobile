using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Players;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// The profile card: what it shows of a new player, who stands on it, what a run and a campaign
/// clear add to it, and who may open whose.
/// </summary>
[Collection(ApiCollection.Name)]
public class ProfileEndpointTests(PromuseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

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

        return (await response.Content.ReadFromJsonAsync<AuthSession>())!;
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession session,
                                           object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method == HttpMethod.Post) request.Headers.Add("Idempotency-Key", Unique);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    private async Task<PlayerProfile> ProfileAsync(AuthSession viewer, Guid? of = null)
    {
        string url = of is null ? "/v1/players/me/profile" : $"/v1/players/{of}/profile";
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, url, viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlayerProfile>())!;
    }

    private async Task<(PlayerState State, string ETag)> StateAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", session));
        response.EnsureSuccessStatusCode();

        return ((await response.Content.ReadFromJsonAsync<PlayerState>())!, response.Headers.ETag!.ToString());
    }

    private async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private Task GiveAsync(AuthSession session, string characterId, int elite = 0, int level = 1) =>
        SqlAsync($"""
            INSERT INTO player_characters (account_id, character_id, elite, level, exp, trust)
            VALUES ({session.PlayerId}, {characterId}, {elite}, {level}, 0, 0)
            ON CONFLICT DO NOTHING
            """);

    /// <summary>Opens a run on the first chart, ages it past the song, and closes it.</summary>
    private async Task PlayAsync(AuthSession session, CompleteRunRequest result)
    {
        HttpResponseMessage started = await _client.SendAsync(Send(HttpMethod.Post, "/v1/runs", session,
            new StartRunRequest(Played.Stage, Played.Operator)));
        started.EnsureSuccessStatusCode();
        RunTicket ticket = (await started.Content.ReadFromJsonAsync<RunTicket>())!;

        await Played.AgeAsync(factory, ticket.RunId, TimeSpan.FromMinutes(5));

        HttpResponseMessage completed = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, result));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    // -------------------------------------------------------------- the card

    [Fact]
    public async Task A_new_players_card_shows_their_name_and_tag_and_nothing_played_yet()
    {
        AuthSession session = await SignInAsync();
        (PlayerState state, _) = await StateAsync(session);

        PlayerProfile card = await ProfileAsync(session);

        Assert.Equal(session.PlayerId, card.PlayerId);
        Assert.Equal(state.DisplayName, card.DisplayName);
        Assert.InRange(card.Tag, 0, 9999);
        Assert.Equal(state.Tag, card.Tag);
        Assert.Equal(1, card.Level);
        Assert.Null(card.Signature);
        Assert.True(card.IsSelf);
        Assert.InRange(card.RegisteredAt, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));

        // No assistant chosen: AMIYA stands on the card, as she does on Home.
        Assert.Equal("AMIYA", card.AssistantId);
        Assert.Equal(1, card.Operators);

        Assert.Null(card.CampaignProgress);
        Assert.Equal(new RhythmRecord(0, 0, 0, null), card.Rhythm);
    }

    [Fact]
    public async Task Anyone_signed_in_can_open_a_card_by_the_id_a_leaderboard_row_carries()
    {
        AuthSession owner = await SignInAsync();
        AuthSession stranger = await SignInAsync();

        PlayerProfile own = await ProfileAsync(owner);
        PlayerProfile seen = await ProfileAsync(stranger, owner.PlayerId);

        Assert.False(seen.IsSelf);
        Assert.Equal(own with { IsSelf = false }, seen);

        // By id, the owner's own card is still their own.
        Assert.True((await ProfileAsync(owner, owner.PlayerId)).IsSelf);

        HttpResponseMessage anonymous = await _client.GetAsync($"/v1/players/{owner.PlayerId}/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task An_unknown_player_and_a_banned_one_are_not_found()
    {
        AuthSession viewer = await SignInAsync();
        AuthSession banned = await SignInAsync();

        HttpResponseMessage unknown = await _client.SendAsync(
            Send(HttpMethod.Get, $"/v1/players/{Guid.NewGuid()}/profile", viewer));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(ErrorCodes.PlayerNotFound, (await unknown.Content.ReadFromJsonAsync<ApiProblem>())!.Code);

        await SqlAsync($"UPDATE accounts SET banned_at = now() WHERE id = {banned.PlayerId}");

        HttpResponseMessage hidden = await _client.SendAsync(
            Send(HttpMethod.Get, $"/v1/players/{banned.PlayerId}/profile", viewer));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        // Their own card still opens for them.
        Assert.Equal(banned.PlayerId, (await ProfileAsync(banned)).PlayerId);
    }

    [Fact]
    public async Task The_chosen_assistant_stands_on_the_card_and_amiya_until_one_is_chosen()
    {
        AuthSession session = await SignInAsync();
        await GiveAsync(session, "NOVA");
        await GiveAsync(session, "ECHO", elite: 1, level: 30);

        // The squad and the operators raised have no say: only the choice does.
        (_, string etag) = await StateAsync(session);
        HttpResponseMessage squad = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/squad", session,
            new SquadRequest([null, "NOVA", "AMIYA", null]), etag));
        squad.EnsureSuccessStatusCode();
        Assert.Equal("AMIYA", (await ProfileAsync(session)).AssistantId);

        HttpResponseMessage desktop = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/desktop-character",
            session, new DesktopCharacterRequest("ECHO"), squad.Headers.ETag!.ToString()));
        desktop.EnsureSuccessStatusCode();

        PlayerProfile card = await ProfileAsync(session);
        Assert.Equal("ECHO", card.AssistantId);
        Assert.Equal(3, card.Operators);

        // Cleared, AMIYA again.
        HttpResponseMessage cleared = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/desktop-character",
            session, new DesktopCharacterRequest(null), desktop.Headers.ETag!.ToString()));
        cleared.EnsureSuccessStatusCode();
        Assert.Equal("AMIYA", (await ProfileAsync(session)).AssistantId);
    }

    // --------------------------------------------------------- the signature

    [Fact]
    public async Task The_owner_writes_a_signature_that_everyone_reads_back_as_the_server_kept_it()
    {
        AuthSession owner = await SignInAsync();
        AuthSession stranger = await SignInAsync();
        (_, string etag) = await StateAsync(owner);

        HttpResponseMessage saved = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/signature", owner,
            new SignatureRequest("  Music\n every day  "), etag));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        PlayerState state = (await saved.Content.ReadFromJsonAsync<PlayerState>())!;
        Assert.Equal("Music every day", state.Signature);
        Assert.NotEqual(etag, saved.Headers.ETag!.ToString());

        Assert.Equal("Music every day", (await ProfileAsync(stranger, owner.PlayerId)).Signature);

        // Blank clears it.
        HttpResponseMessage cleared = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/signature", owner,
            new SignatureRequest(" "), saved.Headers.ETag!.ToString()));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await ProfileAsync(owner)).Signature);
    }

    [Fact]
    public async Task A_signature_write_needs_the_version_and_a_line_that_fits()
    {
        AuthSession session = await SignInAsync();
        (_, string etag) = await StateAsync(session);

        HttpResponseMessage unversioned = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/signature",
            session, new SignatureRequest("hello")));
        Assert.Equal((HttpStatusCode)428, unversioned.StatusCode);

        HttpResponseMessage tooLong = await _client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/signature",
            session, new SignatureRequest(new string('a', 61)), etag));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
        Assert.Contains("signature", (await tooLong.Content.ReadFromJsonAsync<ApiProblem>())!.Errors!.Keys);

        // Nothing was written by either.
        Assert.Null((await ProfileAsync(session)).Signature);
        Assert.Equal(etag, (await StateAsync(session)).ETag);
    }

    // ---------------------------------------------------------------- counts

    [Fact]
    public async Task Runs_and_campaign_clears_show_on_the_card()
    {
        AuthSession session = await SignInAsync();

        await PlayAsync(session, new CompleteRunRequest(true, Played.Win(factory, 1200)));
        await PlayAsync(session, new CompleteRunRequest(true, Played.Win(factory, 900)));
        await PlayAsync(session, new CompleteRunRequest(false, null));

        // Three finished, one chart cleared - with a full combo - and the higher score is the best.
        Assert.Equal(new RhythmRecord(3, 1, 1, 1200), (await ProfileAsync(session)).Rhythm);

        // 按关卡顺序, 不按时间 / Furthest in play order, not the latest cleared.
        await SqlAsync($"""
            INSERT INTO campaign_clears (account_id, stage_id, clears, first_cleared_at)
            VALUES ({session.PlayerId}, 'main_1_1', 1, now()),
                   ({session.PlayerId}, 'main_2_1', 1, now() - interval '1 day'),
                   ({session.PlayerId}, 'main_1_3', 1, now())
            """);

        Assert.Equal("2-1", (await ProfileAsync(session)).CampaignProgress);
    }
}
