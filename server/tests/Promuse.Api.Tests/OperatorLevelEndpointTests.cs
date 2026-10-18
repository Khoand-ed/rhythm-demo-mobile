using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// An operator's level: the run is played and reviewed at the level it opened with.
/// </summary>
[Collection(ApiCollection.Name)]
public class OperatorLevelEndpointTests(PromuseApiFactory factory)
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

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession session, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", Unique);
        return request;
    }

    private async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private Task SetLevelAsync(AuthSession session, int level, string characterId = Played.Operator) =>
        SqlAsync($"UPDATE player_characters SET level = {level} WHERE account_id = {session.PlayerId} AND character_id = {characterId}");

    private async Task<RunTicket> StartAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post, "/v1/runs", session,
            new StartRunRequest(Played.Stage, Played.Operator)));
        response.EnsureSuccessStatusCode();

        RunTicket ticket = (await response.Content.ReadFromJsonAsync<RunTicket>(Json))!;
        await Played.AgeAsync(factory, ticket.RunId, TimeSpan.FromMinutes(5));
        return ticket;
    }

    private async Task<ScoreReview> CompleteAsync(AuthSession session, RunTicket ticket, RunResult result)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post,
            $"/v1/runs/{ticket.RunId}/complete", session, new CompleteRunRequest(true, result)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RunCompletion>(Json))!.Review!;
    }

    // ------------------------------------------------------------- the run

    [Fact]
    public void A_levelled_operator_plays_under_different_rules_than_at_level_one()
    {
        // The premise of everything below: if these matched, a level would change nothing.
        Assert.NotEqual(Played.Win(factory, level: 1).RulesetFingerprint, Played.Win(factory, level: 20).RulesetFingerprint);
    }

    [Fact]
    public async Task A_run_is_reviewed_at_the_level_its_operator_had_when_it_opened()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 20);

        RunTicket ticket = await StartAsync(session);
        Assert.Equal(20, ticket.CharacterLevel);

        ScoreReview review = await CompleteAsync(session, ticket, Played.Win(factory, 1000, level: 20));

        Assert.Equal(ScoreVerdict.Accepted, review.Verdict);
    }

    [Fact]
    public async Task A_result_played_at_another_level_names_the_mismatch()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 20);

        RunTicket ticket = await StartAsync(session);
        ScoreReview review = await CompleteAsync(session, ticket, Played.Win(factory, 1000, level: 1));

        Assert.Equal(ScoreVerdict.Rejected, review.Verdict);
        Assert.Contains(ScoreReviewCodes.RulesetMismatch, review.Reasons);
    }

    [Fact]
    public async Task A_level_up_while_the_song_plays_does_not_change_how_the_run_is_reviewed()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 20);

        RunTicket ticket = await StartAsync(session);
        await SetLevelAsync(session, 30);

        ScoreReview review = await CompleteAsync(session, ticket, Played.Win(factory, 1000, level: 20));

        Assert.Equal(ScoreVerdict.Accepted, review.Verdict);
    }
}
