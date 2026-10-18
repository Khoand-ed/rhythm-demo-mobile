using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Campaign;
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

    /// <summary>
    /// 用 PULSE 测 / The operator these tests level, promote and play: a 3-star, whose cap of 40 and
    /// single promotion the numbers below are written in. New players start with AMIYA, so it is given.
    /// </summary>
    private const string Pulse = "PULSE";

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

        AuthSession session = (await response.Content.ReadFromJsonAsync<AuthSession>(Json))!;
        await SqlAsync($"""
            INSERT INTO player_characters (account_id, character_id, elite, level, exp, trust)
            VALUES ({session.PlayerId}, {Pulse}, 0, 1, 0, 0)
            ON CONFLICT DO NOTHING
            """);
        return session;
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

    private static CharacterState Character(PlayerState state, string id = Pulse) =>
        state.Characters.Single(c => c.CharacterId == id);

    private Task<HttpResponseMessage> PromoteAsync(AuthSession session, int toElite, string characterId = Pulse,
                                                   string? key = null) =>
        _client.SendAsync(Send(HttpMethod.Post, $"/v1/characters/{characterId}/promote", session,
            new PromoteRequest(toElite), key));

    /// <summary>Everything PULSE's promotion to Elite 1 costs, and a little to spare.</summary>
    private async Task GivePromotionAsync(AuthSession session, int spare = 0)
    {
        foreach (ItemStack item in OperatorLevels.PromotionCost(3, 1)!)
        {
            await GiveAsync(session, item.ItemId, item.Amount + spare);
        }
    }

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>(Json))!.Code;

    private async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private Task SetLevelAsync(AuthSession session, int level, string characterId = Pulse) =>
        SqlAsync($"UPDATE player_characters SET level = {level} WHERE account_id = {session.PlayerId} AND character_id = {characterId}");

    private async Task<RunTicket> StartAsync(AuthSession session)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Post, "/v1/runs", session,
            new StartRunRequest(Played.Stage, Pulse)));
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

        HttpResponseMessage response = await LevelUpAsync(session, Pulse, null, (16, 1), (11, 1));
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

        HttpResponseMessage response = await LevelUpAsync(session, Pulse, null, (11, 1));

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
        HttpResponseMessage both = await LevelUpAsync(session, Pulse, null, (16, 1), (11, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, both.StatusCode);

        // The 2000 alone reaches it: 1120 is lost, and only the 880 applied is paid for.
        HttpResponseMessage one = await LevelUpAsync(session, Pulse, null, (16, 1));
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

        HttpResponseMessage poor = await LevelUpAsync(session, Pulse, null, (16, 1));
        Assert.Equal(HttpStatusCode.Conflict, poor.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientFunds, await CodeOf(poor));

        await GiveAsync(session, OperatorLevels.LmdItemId, 5000);
        HttpResponseMessage tooFew = await LevelUpAsync(session, Pulse, null, (16, 2));
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

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await LevelUpAsync(session, Pulse, null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await LevelUpAsync(session, Pulse, null, (OperatorLevels.LmdItemId, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await LevelUpAsync(session, Pulse, null, (11, 1), (11, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await LevelUpAsync(session, Pulse, null, (11, 0))).StatusCode);

        HttpResponseMessage stranger = await LevelUpAsync(session, "NOVA", null, (11, 1));
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

        HttpResponseMessage first = await LevelUpAsync(session, Pulse, key, (11, 1));
        HttpResponseMessage again = await LevelUpAsync(session, Pulse, key, (11, 1));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Equal(1, Amount(await StateAsync(session), 11));
    }

    // ------------------------------------------------------------- promote

    [Fact]
    public async Task Promotion_at_the_cap_raises_it_keeps_the_level_and_costs_exactly_the_table()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 40);
        await GivePromotionAsync(session, spare: 1);

        HttpResponseMessage response = await PromoteAsync(session, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PromoteResult result = (await response.Content.ReadFromJsonAsync<PromoteResult>(Json))!;

        Assert.Equal((0, 1, 40, 55), (result.FromElite, result.ToElite, result.Level, result.MaxLevel));
        Assert.Equal((1, 40), (Character(result.Player).Elite, Character(result.Player).Level));

        // One of each left over: exactly the table was taken.
        Assert.All(OperatorLevels.PromotionCost(3, 1)!, item => Assert.Equal(1, Amount(result.Player, item.ItemId)));
    }

    [Fact]
    public async Task After_promotion_levelling_goes_past_the_old_cap_on_the_new_phases_curve()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 40);
        await GivePromotionAsync(session);
        Assert.Equal(HttpStatusCode.OK, (await PromoteAsync(session, 1)).StatusCode);

        await GiveAsync(session, 16, 1);
        await GiveAsync(session, OperatorLevels.LmdItemId, 5000);

        HttpResponseMessage response = await LevelUpAsync(session, Pulse, null, (16, 1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelUpResult result = (await response.Content.ReadFromJsonAsync<LevelUpResult>(Json))!;

        LevelGain expected = OperatorLevels.Gain(1, 40, 0, 55, 2000);
        Assert.Equal(expected.Level, result.ToLevel);
        Assert.Equal(OperatorLevels.LmdFor(1, 2000), result.LmdSpent);
    }

    [Fact]
    public async Task Promotion_below_the_cap_or_past_the_last_phase_is_refused_and_nothing_is_spent()
    {
        AuthSession session = await SignInAsync();
        await GivePromotionAsync(session);
        await SetLevelAsync(session, 39);

        HttpResponseMessage low = await PromoteAsync(session, 1);
        Assert.Equal(HttpStatusCode.Conflict, low.StatusCode);
        Assert.Equal(ErrorCodes.BelowLevelCap, await CodeOf(low));

        // PULSE is a 3-star: Elite 1 is as far as it goes.
        await SqlAsync($"UPDATE player_characters SET elite = 1, level = 55 WHERE account_id = {session.PlayerId}");
        HttpResponseMessage top = await PromoteAsync(session, 2);
        Assert.Equal(HttpStatusCode.Conflict, top.StatusCode);
        Assert.Equal(ErrorCodes.EliteCapped, await CodeOf(top));

        PlayerState state = await StateAsync(session);
        Assert.All(OperatorLevels.PromotionCost(3, 1)!, item => Assert.Equal(item.Amount, Amount(state, item.ItemId)));
    }

    [Fact]
    public async Task Short_of_materials_or_naming_the_wrong_phase_nothing_happens()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 40);
        await GiveAsync(session, OperatorLevels.LmdItemId, 100000);
        await GiveAsync(session, OperatorLevels.OrirockItemId, 100);

        HttpResponseMessage poor = await PromoteAsync(session, 1);
        Assert.Equal(HttpStatusCode.Conflict, poor.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientFunds, await CodeOf(poor));

        await GiveAsync(session, OperatorLevels.SugarSubstituteItemId, 100);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PromoteAsync(session, 2)).StatusCode);

        PlayerState state = await StateAsync(session);
        Assert.Equal(0, Character(state).Elite);
        Assert.Equal(100000, Amount(state, OperatorLevels.LmdItemId));
        Assert.Equal(100, Amount(state, OperatorLevels.OrirockItemId));
    }

    [Fact]
    public async Task A_retried_promotion_replays_the_first_answer_instead_of_paying_twice()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 40);
        await GivePromotionAsync(session, spare: 100000);
        string key = Unique;

        HttpResponseMessage first = await PromoteAsync(session, 1, key: key);
        HttpResponseMessage again = await PromoteAsync(session, 1, key: key);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());

        // Paid once: the spare is what is left.
        Assert.Equal(100000, Amount(await StateAsync(session), OperatorLevels.LmdItemId));
    }

    [Fact]
    public async Task The_seeded_campaign_drops_every_battle_record_and_promotion_material()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/campaign", session));
        response.EnsureSuccessStatusCode();
        CampaignView view = (await response.Content.ReadFromJsonAsync<CampaignView>(Json))!;

        // The migrations updated the campaign the database was seeded with: every record, and the
        // promotion materials besides LMD.
        HashSet<int> dropped = [.. view.Chapters.SelectMany(c => c.Stages).SelectMany(s => s.Stage.Drops).Select(d => d.ItemId)];
        Assert.All(OperatorLevels.Records, r => Assert.Contains(r.ItemId, dropped));
        Assert.Contains(OperatorLevels.OrirockItemId, dropped);
        Assert.Contains(OperatorLevels.SugarSubstituteItemId, dropped);
    }

    // ------------------------------------------------------------- the run

    [Fact]
    public void A_levelled_operator_plays_under_different_rules_than_at_level_one()
    {
        // The premise of everything below: if these matched, a level would change nothing.
        Assert.NotEqual(Played.Win(factory, characterId: Pulse, level: 1).RulesetFingerprint, Played.Win(factory, characterId: Pulse, level: 20).RulesetFingerprint);
    }

    [Fact]
    public async Task A_run_is_reviewed_at_the_level_its_operator_had_when_it_opened()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 20);

        RunTicket ticket = await StartAsync(session);
        Assert.Equal(20, ticket.CharacterLevel);

        ScoreReview review = await CompleteAsync(session, ticket, Played.Win(factory, characterId: Pulse, level: 20));

        Assert.Equal(ScoreVerdict.Accepted, review.Verdict);
    }

    [Fact]
    public async Task A_result_played_at_another_level_names_the_mismatch()
    {
        AuthSession session = await SignInAsync();
        await SetLevelAsync(session, 20);

        RunTicket ticket = await StartAsync(session);
        ScoreReview review = await CompleteAsync(session, ticket, Played.Win(factory, characterId: Pulse, level: 1));

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

        ScoreReview review = await CompleteAsync(session, ticket, Played.Win(factory, characterId: Pulse, level: 20));

        Assert.Equal(ScoreVerdict.Accepted, review.Verdict);
    }
}
