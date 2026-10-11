using System.Net;
using System.Net.Http.Json;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Campaign;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// The main campaign end to end, on the seeded campaign: 1-1 plays stage_001, which the harness can
/// win; later stages are cleared by recording the clear, as the event tests do.
/// </summary>
[Collection(ApiCollection.Name)]
public class CampaignEndpointTests(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    private const int Lmd = 2, OriginitePrime = 0, Orundum = 1;

    private async Task<CampaignView> ViewAsync(AuthSession player) =>
        await ReadAsync<CampaignView>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/campaign", player)));

    private Task<HttpResponseMessage> StartAsync(AuthSession player, string chart, string? campaignStage) =>
        Client.SendAsync(Send(HttpMethod.Post, "/v1/runs", player, new StartRunRequest(chart, Played.Operator, null, campaignStage)));

    private async Task<RunCompletion> WinAsync(AuthSession player, string? campaignStage, bool won = true)
    {
        HttpResponseMessage start = await StartAsync(player, Played.Stage, campaignStage);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        RunTicket ticket = await ReadAsync<RunTicket>(start);
        await Played.AgeAsync(Factory, ticket.RunId, TimeSpan.FromMinutes(5));

        HttpResponseMessage done = await Client.SendAsync(Send(HttpMethod.Post, $"/v1/runs/{ticket.RunId}/complete", player,
            new CompleteRunRequest(won, won ? Played.Win(Factory) : null)));
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        return await ReadAsync<RunCompletion>(done);
    }

    [Fact]
    public async Task A_new_player_sees_only_the_first_stage_open()
    {
        (AuthSession player, _) = await PlayerAsync();
        CampaignView view = await ViewAsync(player);

        Assert.Equal(2, view.Chapters.Count);
        Assert.True(view.Chapters[0].IsUnlocked);
        Assert.False(view.Chapters[1].IsUnlocked);
        Assert.Equal([true, false, false], view.Chapters[0].Stages.Select(s => s.IsUnlocked));
        Assert.Equal(6, view.Chapters[0].Stages[0].StaminaCost);
        Assert.Equal("1-1", view.Chapters[0].Stages[0].Stage.Code);
        Assert.NotEmpty(view.Chapters[0].Stages[0].Stage.Drops);
    }

    [Fact]
    public async Task A_locked_stage_or_the_wrong_chart_is_refused_before_anything_is_charged()
    {
        (AuthSession player, _) = await PlayerAsync();

        HttpResponseMessage locked = await StartAsync(player, "stage_AIW", "main_1_2");
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal(ErrorCodes.StageLocked, await CodeAsync(locked));

        HttpResponseMessage wrongChart = await StartAsync(player, "stage_AIW", "main_1_1");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongChart.StatusCode);

        HttpResponseMessage both = await Client.SendAsync(Send(HttpMethod.Post, "/v1/runs", player,
            new StartRunRequest(Played.Stage, Played.Operator, "some_event", "main_1_1")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, both.StatusCode);

        // 没扣理智 / Nothing was charged for any of them.
        Assert.Equal(0, (await ViewAsync(player)).Chapters[0].Stages.Count(s => s.IsCleared));
    }

    [Fact]
    public async Task The_first_clear_pays_orundum_once_and_every_clear_rolls_drops_and_exp()
    {
        (AuthSession player, _) = await PlayerAsync();
        int orundum = await BalanceAsync(player.PlayerId, Orundum);

        RunCompletion first = await WinAsync(player, "main_1_1");
        RunRewards rewards = first.Rewards!;
        Assert.Equal(50, rewards.FirstClearOrundum);
        Assert.Contains(rewards.Drops, d => d.ItemId == Lmd && d.Amount is >= 300 and <= 500);
        Assert.Equal(6 * 12, rewards.Exp);
        Assert.Equal(orundum + 50, await BalanceAsync(player.PlayerId, Orundum));

        CampaignView view = await ViewAsync(player);
        Assert.True(view.Chapters[0].Stages[0].IsCleared);
        Assert.True(view.Chapters[0].Stages[1].IsUnlocked);

        RunCompletion again = await WinAsync(player, "main_1_1");
        Assert.Equal(0, again.Rewards!.FirstClearOrundum);
        Assert.Contains(again.Rewards.Drops, d => d.ItemId == Lmd);
        Assert.Equal(orundum + 50, await BalanceAsync(player.PlayerId, Orundum));
    }

    [Fact]
    public async Task The_same_chart_off_the_campaign_pays_exp_but_clears_nothing()
    {
        (AuthSession player, _) = await PlayerAsync();

        RunCompletion plain = await WinAsync(player, null);
        Assert.Equal((0, 0, 72), (plain.Rewards!.FirstClearOrundum, plain.Rewards.Drops.Count, plain.Rewards.Exp));
        Assert.False((await ViewAsync(player)).Chapters[0].Stages[0].IsCleared);
    }

    [Fact]
    public async Task A_failed_run_pays_nothing()
    {
        (AuthSession player, _) = await PlayerAsync();

        RunCompletion lost = await WinAsync(player, "main_1_1", won: false);
        Assert.Null(lost.Rewards);
        Assert.False((await ViewAsync(player)).Chapters[0].Stages[0].IsCleared);
    }

    [Fact]
    public async Task Clearing_chapter_one_opens_chapter_two()
    {
        (AuthSession player, _) = await PlayerAsync();
        foreach (string stage in new[] { "main_1_1", "main_1_2", "main_1_3" })
        {
            await SqlAsync($"""
                INSERT INTO campaign_clears (account_id, stage_id, clears, first_cleared_at)
                VALUES ({player.PlayerId}, {stage}, 1, now())
                """);
        }

        CampaignView view = await ViewAsync(player);
        Assert.True(view.Chapters[1].IsUnlocked);
        Assert.True(view.Chapters[1].Stages[0].IsUnlocked);
        Assert.False(view.Chapters[1].Stages[1].IsUnlocked);
    }

    [Fact]
    public async Task A_level_up_pays_originite_prime_and_fills_sanity()
    {
        (AuthSession player, _) = await PlayerAsync();
        int need = PlayerProgression.MaxExp(9);
        await SqlAsync($"UPDATE players SET level = 9, exp = {need - 10}, stamina = 20, stamina_updated_at = now() WHERE account_id = {player.PlayerId}");
        int prime = await BalanceAsync(player.PlayerId, OriginitePrime);

        RunCompletion done = await WinAsync(player, null);

        Assert.Equal((9, 10, 2), (done.Rewards!.LevelBefore, done.Rewards.LevelAfter, done.Rewards.PrimeFromLevels));
        Assert.Equal(10, done.Player.Level);
        Assert.Equal(prime + 2, await BalanceAsync(player.PlayerId, OriginitePrime));

        // 升级回满 / Sanity is full at level 10's ceiling.
        Assert.Equal(PlayerProgression.MaxStamina(10), done.Player.Stamina.Current);
    }

    [Fact]
    public async Task Only_admins_edit_the_campaign_and_saves_need_the_version()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/campaign", player))).StatusCode);

        AdminCampaign current = await ReadAsync<AdminCampaign>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/campaign", admin)));

        HttpResponseMessage stale = await Client.SendAsync(Send(HttpMethod.Put, "/v1/admin/campaign", admin,
            new CampaignUpsertRequest(current.Definition), ifMatch: $"W/\"{current.Version + 50}\""));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        CampaignChapter first = current.Definition.Chapters[0];
        CampaignDefinition broken = new([first with { Stages = [first.Stages[0] with { ChartId = "no_such_chart" }] }]);
        HttpResponseMessage invalid = await Client.SendAsync(Send(HttpMethod.Put, "/v1/admin/campaign", admin,
            new CampaignUpsertRequest(broken), ifMatch: $"W/\"{current.Version}\""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        // The same document saved back: a new version, nothing else changes for the other tests.
        HttpResponseMessage saved = await Client.SendAsync(Send(HttpMethod.Put, "/v1/admin/campaign", admin,
            new CampaignUpsertRequest(current.Definition), ifMatch: $"W/\"{current.Version}\""));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(current.Version + 1, (await ReadAsync<AdminCampaign>(saved)).Version);
    }
}
