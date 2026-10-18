using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Characters;
using Promuse.Contracts.Players;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// An operator's level: spending battle records and LMD to raise it, and the run being played and
/// reviewed at the level it opened with.
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

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession session, object? body = null,
                                           string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", key ?? Unique);
        return request;
    }

    private Task GiveAsync(AuthSession session, int itemId, int amount) =>
        SqlAsync($"""
            INSERT INTO player_items (account_id, item_id, amount) VALUES ({session.PlayerId}, {itemId}, {amount})
            ON CONFLICT (account_id, item_id) DO UPDATE SET amount = EXCLUDED.amount
            """);

    private Task<HttpResponseMessage> LevelUpAsync(AuthSession session, string characterId, string? key,
                                                   params (int ItemId, int Amount)[] records) =>
        _client.SendAsync(Send(HttpMethod.Post, $"/v1/characters/{characterId}/level-up", session,
            new LevelUpRequest([.. records.Select(r => new ItemStack(r.ItemId, r.Amount))]), key));

    private async Task<PlayerState> StateAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", session));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlayerState>(Json))!;
    }

    private static int Amount(PlayerState state, int itemId) =>
        state.Inventory.FirstOrDefault(i => i.ItemId == itemId)?.Amount ?? 0;

    private static CharacterState Character(PlayerState state, string id = Played.Operator) =>
        state.Characters.Single(c => c.CharacterId == id);

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>(Json))!.Code;

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

    // ------------------------------------------------------------ level up

    [Fact]
    public async Task Records_and_lmd_buy_exactly_the_levels_the_shared_rules_say()
    {
        AuthSession session = await SignInAsync();
        await GiveAsync(session, 11, 3);
        await GiveAsync(session, 16, 1);
        await GiveAsync(session, OperatorLevels.LmdItemId, 5000);

        HttpResponseMessage response = await LevelUpAsync(session, Played.Operator, null, (16, 1), (11, 1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelUpResult result = (await response.Content.ReadFromJsonAsync<LevelUpResult>(Json))!;

        LevelGain expected = OperatorLevels.Gain(0, 1, 0, 40, 2200);
        Assert.Equal((1, expected.Level, 2200), (result.FromLevel, result.ToLevel, result.ExpApplied));
        Assert.Equal(OperatorLevels.LmdFor(0, 2200), result.LmdSpent);

        PlayerState state = await StateAsync(session);
        Assert.Equal((expected.Level, expected.Exp), (Character(state).Level, Character(state).Exp));
        Assert.Equal(2, Amount(state, 11));
        Assert.Equal(0, Amount(state, 16));
        Assert.Equal(5000 - result.LmdSpent, Amount(state, OperatorLevels.LmdItemId));
    }

    [Fact]
    public async Task An_operator_at_its_cap_is_refused_before_anything_is_spent()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 40);
        await GiveAsync(session, 11, 1);

        HttpResponseMessage response = await LevelUpAsync(session, Played.Operator, null, (11, 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.LevelCapped, await CodeOf(response));
        Assert.Equal(1, Amount(await StateAsync(session), 11));
    }

    [Fact]
    public async Task Reaching_the_cap_may_lose_some_exp_but_never_a_whole_record()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 39);
        await GiveAsync(session, 11, 1);
        await GiveAsync(session, 16, 1);
        await GiveAsync(session, OperatorLevels.LmdItemId, 5000);

        // 2000 + 200 EXP for the 880 level 39 needs: the 200 lands entirely past the cap.
        HttpResponseMessage both = await LevelUpAsync(session, Played.Operator, null, (16, 1), (11, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, both.StatusCode);

        // The 2000 alone reaches it: 1120 is lost, and only the 880 applied is paid for.
        HttpResponseMessage one = await LevelUpAsync(session, Played.Operator, null, (16, 1));
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        LevelUpResult result = (await one.Content.ReadFromJsonAsync<LevelUpResult>(Json))!;
        Assert.Equal((40, 880, OperatorLevels.LmdFor(0, 880)), (result.ToLevel, result.ExpApplied, result.LmdSpent));
        Assert.Equal(0, Character(result.Player).Exp);
    }

    [Fact]
    public async Task Short_of_lmd_or_records_nothing_is_spent_and_nothing_levels()
    {
        AuthSession session = await SignInAsync();
        await GiveAsync(session, 16, 1);
        await GiveAsync(session, OperatorLevels.LmdItemId, 10);

        HttpResponseMessage poor = await LevelUpAsync(session, Played.Operator, null, (16, 1));
        Assert.Equal(HttpStatusCode.Conflict, poor.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientFunds, await CodeOf(poor));

        await GiveAsync(session, OperatorLevels.LmdItemId, 5000);
        HttpResponseMessage tooFew = await LevelUpAsync(session, Played.Operator, null, (16, 2));
        Assert.Equal(HttpStatusCode.Conflict, tooFew.StatusCode);

        PlayerState state = await StateAsync(session);
        Assert.Equal(1, Character(state).Level);
        Assert.Equal(1, Amount(state, 16));
        Assert.Equal(5000, Amount(state, OperatorLevels.LmdItemId));
    }

    [Fact]
    public async Task Only_battle_records_once_each_and_only_an_owned_operator()
    {
        AuthSession session = await SignInAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await LevelUpAsync(session, Played.Operator, null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await LevelUpAsync(session, Played.Operator, null, (OperatorLevels.LmdItemId, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await LevelUpAsync(session, Played.Operator, null, (11, 1), (11, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await LevelUpAsync(session, Played.Operator, null, (11, 0))).StatusCode);

        HttpResponseMessage stranger = await LevelUpAsync(session, "AMIYA", null, (11, 1));
        Assert.Equal(HttpStatusCode.Conflict, stranger.StatusCode);
        Assert.Equal(ErrorCodes.CharacterNotOwned, await CodeOf(stranger));
    }

    [Fact]
    public async Task A_retried_level_up_replays_the_first_answer_instead_of_spending_twice()
    {
        AuthSession session = await SignInAsync();
        await GiveAsync(session, 11, 2);
        await GiveAsync(session, OperatorLevels.LmdItemId, 5000);
        string key = Unique;

        HttpResponseMessage first = await LevelUpAsync(session, Played.Operator, key, (11, 1));
        HttpResponseMessage again = await LevelUpAsync(session, Played.Operator, key, (11, 1));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Equal(1, Amount(await StateAsync(session), 11));
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
