using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Api.Manufacture;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Characters;
using Promuse.Contracts.Items;
using Promuse.Contracts.Manufacture;
using Promuse.Contracts.Players;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// Manufacture end to end: unlocking it, what a station holds, collecting, raising a station, and
/// every way the server says no - against Postgres, with the row locks and transactions it really runs.
///
/// 拨时间 / No test waits for a station. Each one moves a station's anchor back by the time it needs,
/// in whole seconds and well clear of a unit boundary, and reads what that time made.
///
/// 开关要还原 / Manufacture is off by default, so each test switches it on first and off again
/// after: the config is global, and the other classes in the collection run against it afterwards.
/// </summary>
[Collection(ApiCollection.Name)]
public class ManufactureEndpointTests(PromuseApiFactory factory) : IAsyncLifetime
{
    private const int Lmd = OperatorLevels.LmdItemId;
    private const int BasicRecord = 11;
    private const int FrontlineRecord = 14;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => SwitchAsync(on: true);

    public Task DisposeAsync() => SwitchAsync(on: false);

    // --------------------------------------------------------------- helpers

    private Task SwitchAsync(bool on) => ManufactureFlag.SwitchAsync(factory, on, nameof(ManufactureEndpointTests));

    private async Task<AuthSession> GuestAsync()
    {
        HttpResponseMessage response = await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/auth/guest", session: null, new GuestSignInRequest(Unique)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSession>(Json))!;
    }

    /// <summary>A guest holding Manufacture Blueprint I, whose stations a first look has created.</summary>
    private async Task<AuthSession> UnlockedAsync()
    {
        AuthSession player = await GuestAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint1, 1);
        await ViewAsync(player);
        return player;
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession? session, object? body = null,
                                           string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (session is not null) request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", key ?? Unique);
        return request;
    }

    private async Task<ManufactureView> ViewAsync(AuthSession player)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/manufacture", player));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ManufactureView>(Json))!;
    }

    private Task<HttpResponseMessage> CollectAsync(AuthSession player, string? stationId, string? key = null) =>
        _client.SendAsync(Send(HttpMethod.Post, "/v1/manufacture/collect", player, new CollectRequest(stationId), key));

    private Task<HttpResponseMessage> UpgradeAsync(AuthSession player, string stationId, int fromLevel, string? key = null) =>
        _client.SendAsync(Send(HttpMethod.Post, "/v1/manufacture/upgrade", player, new UpgradeRequest(stationId, fromLevel), key));

    private static async Task<ManufactureActionResult> ResultOf(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ManufactureActionResult>(Json))!;
    }

    private static async Task<ApiProblem> ProblemOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiProblem>(Json))!;

    private static StationState Station(ManufactureView view, string stationId) =>
        view.Stations.Single(s => s.StationId == stationId);

    private static int Amount(PlayerState state, int itemId) =>
        state.Inventory.FirstOrDefault(i => i.ItemId == itemId)?.Amount ?? 0;

    /// <summary>Sets a stack to exactly <paramref name="amount"/>.</summary>
    private Task GiveAsync(AuthSession player, int itemId, int amount) =>
        SqlAsync($"""
            INSERT INTO player_items (account_id, item_id, amount) VALUES ({player.PlayerId}, {itemId}, {amount})
            ON CONFLICT (account_id, item_id) DO UPDATE SET amount = EXCLUDED.amount
            """);

    private async Task<int> HeldAsync(AuthSession player, int itemId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        return await db.PlayerItems
            .Where(i => i.AccountId == player.PlayerId && i.ItemId == itemId)
            .Select(i => i.Amount)
            .FirstOrDefaultAsync();
    }

    /// <summary>Makes a station count from <paramref name="ago"/> before now; returns that moment.</summary>
    private async Task<DateTimeOffset> AnchorAsync(AuthSession player, string stationId, TimeSpan ago)
    {
        DateTimeOffset anchor = ManufactureClock.WholeSeconds(DateTimeOffset.UtcNow - ago);
        await SqlAsync($"""
            UPDATE manufacture_stations SET anchor_at = {anchor}
            WHERE account_id = {player.PlayerId} AND station_id = {stationId}
            """);
        return anchor;
    }

    /// <summary>Records a first clear of <paramref name="stageId"/>, as the campaign does when a run clears it.</summary>
    private Task ClearedAsync(AuthSession player, string stageId, DateTimeOffset? at = null) =>
        SqlAsync($"""
            INSERT INTO campaign_clears (account_id, stage_id, clears, first_cleared_at)
            VALUES ({player.PlayerId}, {stageId}, 1, {at ?? DateTimeOffset.UtcNow})
            """);

    private Task SetLevelAsync(AuthSession player, string stationId, int level) =>
        SqlAsync($"""
            UPDATE manufacture_stations SET level = {level}
            WHERE account_id = {player.PlayerId} AND station_id = {stationId}
            """);

    private async Task<int> StationRowsAsync(AuthSession player)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        return await db.ManufactureStations.CountAsync(s => s.AccountId == player.PlayerId);
    }

    private async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    /// <summary>Within a few seconds of <paramref name="expected"/>: for moments the server took from its own clock.</summary>
    private static void Near(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value, expected.AddSeconds(-5), expected.AddSeconds(5));
    }

    // ------------------------------------------------------------- unlocking

    [Fact]
    public async Task Without_blueprint_one_manufacture_is_locked_and_names_the_stage_that_opens_it()
    {
        AuthSession player = await GuestAsync();

        ManufactureView view = await ViewAsync(player);
        Assert.False(view.IsUnlocked);
        Assert.Equal("1-1", view.UnlockStageCode);
        Assert.Empty(view.Stations);
        Assert.Null(view.Player);

        foreach (HttpResponseMessage refused in new[]
                 {
                     await CollectAsync(player, null),
                     await UpgradeAsync(player, ManufactureStations.Lmd, 1),
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            ApiProblem problem = await ProblemOf(refused);
            Assert.Equal(ErrorCodes.ManufactureLocked, problem.Code);
            Assert.Contains("1-1", problem.Detail);
        }

        Assert.Equal(0, await StationRowsAsync(player));
    }

    [Fact]
    public async Task Blueprint_one_opens_two_stations_at_level_one()
    {
        AuthSession player = await UnlockedAsync();
        ManufactureView view = await ViewAsync(player);

        Assert.True(view.IsUnlocked);
        Assert.Null(view.UnlockStageCode);
        Assert.Equal([ManufactureStations.Lmd, ManufactureStations.Exp], view.Stations.Select(s => s.StationId));
        Assert.Equal(2, await StationRowsAsync(player));

        foreach (StationState station in view.Stations)
        {
            Assert.Equal(1, station.Level);
            Assert.Equal(ManufactureRules.MaxLevel, station.MaxLevel);
            Assert.Equal(ManufactureRules.LevelOf(station.StationId, 1), station.Current);
            Assert.Equal(0, station.StoredUnits);
            Assert.Equal(station.AnchorAt.AddSeconds(station.Current.SecondsPerUnit), station.NextUnitAt);
            Assert.Equal(station.AnchorAt.AddSeconds((long)station.Current.SecondsPerUnit * station.Current.CapacityUnits),
                         station.FullAt);

            StationUpgrade next = station.Next!;
            Assert.Equal(ManufactureRules.LevelOf(station.StationId, 2), next.To);
            Assert.Equal([new ItemStack(Lmd, 8_000)], next.Cost);
            Assert.Equal(KeyItems.ManufactureBlueprint2, next.BlueprintItemId);
            Assert.Equal("1-3", next.BlueprintStageCode);
        }
    }

    [Fact]
    public async Task Stations_count_from_the_first_clear_of_the_stage_that_opened_them()
    {
        AuthSession player = await GuestAsync();
        DateTimeOffset cleared = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(-10);
        await ClearedAsync(player, ManufactureRules.UnlockStageId, cleared);

        ManufactureView view = await ViewAsync(player);

        // Three hours ten: six LMD units of 30 minutes, one Basic record of two hours.
        Assert.Equal(ManufactureClock.WholeSeconds(cleared), Station(view, ManufactureStations.Lmd).AnchorAt);
        Assert.Equal(6, Station(view, ManufactureStations.Lmd).StoredUnits);
        Assert.Equal(1, Station(view, ManufactureStations.Exp).StoredUnits);
    }

    // ---------------------------------------------------------------- collect

    [Fact]
    public async Task Collecting_pays_whole_units_and_keeps_the_part_still_being_made()
    {
        AuthSession player = await UnlockedAsync();
        int lmdBefore = await HeldAsync(player, Lmd);
        int versionBefore = (await PlayerStateAsync(player)).StateVersion;

        // Seventy minutes: two units, and ten minutes into the third.
        DateTimeOffset anchor = await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(70));
        Assert.Equal(2, Station(await ViewAsync(player), ManufactureStations.Lmd).StoredUnits);

        ManufactureActionResult result = await ResultOf(await CollectAsync(player, ManufactureStations.Lmd));

        Assert.Equal([new ItemStack(Lmd, 100)], result.Granted);
        Assert.Equal(lmdBefore + 100, Amount(result.Player, Lmd));
        Assert.Equal(lmdBefore + 100, await HeldAsync(player, Lmd));
        Assert.True(result.Player.StateVersion > versionBefore, "the bag changed, so the save's version moves");

        StationState lmd = Station(result.View, ManufactureStations.Lmd);
        Assert.Equal(0, lmd.StoredUnits);
        Assert.Equal(anchor.AddMinutes(60), lmd.AnchorAt);
        Assert.Equal(anchor.AddMinutes(90), lmd.NextUnitAt);
    }

    private async Task<PlayerState> PlayerStateAsync(AuthSession player)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", player));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlayerState>(Json))!;
    }

    [Fact]
    public async Task Collecting_an_empty_station_is_refused()
    {
        AuthSession player = await UnlockedAsync();

        foreach (string? stationId in new[] { ManufactureStations.Lmd, null })
        {
            HttpResponseMessage response = await CollectAsync(player, stationId);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ErrorCodes.NothingToClaim, (await ProblemOf(response)).Code);
        }
    }

    [Fact]
    public async Task Collecting_everything_takes_from_every_station_that_has_something()
    {
        AuthSession player = await UnlockedAsync();
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));

        // Only the LMD station has made something: the record station is simply passed over.
        ManufactureActionResult one = await ResultOf(await CollectAsync(player, null));
        Assert.Equal([new ItemStack(Lmd, 50)], one.Granted);

        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));
        await AnchorAsync(player, ManufactureStations.Exp, TimeSpan.FromHours(5));
        int basicBefore = await HeldAsync(player, BasicRecord);

        ManufactureActionResult both = await ResultOf(await CollectAsync(player, null));
        Assert.Equal(2, both.Granted.Count);
        Assert.Contains(new ItemStack(Lmd, 50), both.Granted);
        Assert.Contains(new ItemStack(BasicRecord, 2), both.Granted);
        Assert.Equal(basicBefore + 2, Amount(both.Player, BasicRecord));
        Assert.All(both.View.Stations, s => Assert.Equal(0, s.StoredUnits));
    }

    [Fact]
    public async Task A_full_store_stops_and_restarts_from_the_visit()
    {
        AuthSession player = await UnlockedAsync();
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromHours(12));

        // Full after 8 hours; the 4 since have made nothing.
        StationState full = Station(await ViewAsync(player), ManufactureStations.Lmd);
        Assert.Equal(16, full.StoredUnits);
        Assert.Null(full.NextUnitAt);
        Assert.Null(full.FullAt);

        ManufactureActionResult result = await ResultOf(await CollectAsync(player, ManufactureStations.Lmd));
        Assert.Equal([new ItemStack(Lmd, 800)], result.Granted);
        Near(DateTimeOffset.UtcNow.AddMinutes(30), Station(result.View, ManufactureStations.Lmd).NextUnitAt);
    }

    [Fact]
    public async Task An_anchor_in_the_future_reads_as_empty()
    {
        AuthSession player = await UnlockedAsync();
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromHours(-2));

        Assert.Equal(0, Station(await ViewAsync(player), ManufactureStations.Lmd).StoredUnits);
    }

    [Fact]
    public async Task A_retried_collect_is_answered_from_the_first_and_pays_once()
    {
        AuthSession player = await UnlockedAsync();
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));
        int before = await HeldAsync(player, Lmd);
        string key = Unique;

        ManufactureActionResult first = await ResultOf(await CollectAsync(player, ManufactureStations.Lmd, key));
        ManufactureActionResult again = await ResultOf(await CollectAsync(player, ManufactureStations.Lmd, key));

        Assert.Equal(first.Granted, again.Granted);
        Assert.Equal(before + 50, await HeldAsync(player, Lmd));

        HttpResponseMessage reused = await CollectAsync(player, ManufactureStations.Exp, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal(ErrorCodes.IdempotencyKeyConflict, (await ProblemOf(reused)).Code);
    }

    [Fact]
    public async Task Two_collects_at_once_pay_once()
    {
        AuthSession player = await UnlockedAsync();
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));
        int before = await HeldAsync(player, Lmd);

        HttpResponseMessage[] responses = await Task.WhenAll(
            CollectAsync(player, ManufactureStations.Lmd),
            CollectAsync(player, ManufactureStations.Lmd));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(before + 50, await HeldAsync(player, Lmd));
    }

    // ---------------------------------------------------------------- upgrade

    [Fact]
    public async Task Raising_a_station_needs_the_next_blueprint()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, Lmd, 10_000);

        HttpResponseMessage response = await UpgradeAsync(player, ManufactureStations.Lmd, 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ApiProblem problem = await ProblemOf(response);
        Assert.Equal(ErrorCodes.BlueprintRequired, problem.Code);
        Assert.Contains("1-3", problem.Detail);
        Assert.Equal(1, Station(await ViewAsync(player), ManufactureStations.Lmd).Level);
        Assert.Equal(10_000, await HeldAsync(player, Lmd));
    }

    [Fact]
    public async Task Raising_a_station_without_the_lmd_changes_nothing_not_even_the_collection_before_it()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, Lmd, 7_000);
        DateTimeOffset anchor = await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));

        // 7,000 and the 50 the station holds is still short of 8,000.
        HttpResponseMessage response = await UpgradeAsync(player, ManufactureStations.Lmd, 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientFunds, (await ProblemOf(response)).Code);

        StationState lmd = Station(await ViewAsync(player), ManufactureStations.Lmd);
        Assert.Equal(1, lmd.Level);
        Assert.Equal(1, lmd.StoredUnits);
        Assert.Equal(anchor, lmd.AnchorAt);
        Assert.Equal(7_000, await HeldAsync(player, Lmd));
    }

    [Fact]
    public async Task Lmd_the_station_made_counts_toward_raising_it_and_the_blueprint_stays()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, Lmd, 7_990);
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));

        ManufactureActionResult result = await ResultOf(await UpgradeAsync(player, ManufactureStations.Lmd, 1));

        Assert.Equal([new ItemStack(Lmd, 50)], result.Granted);
        Assert.Equal(40, Amount(result.Player, Lmd));
        Assert.Equal(1, Amount(result.Player, KeyItems.ManufactureBlueprint2));

        StationState lmd = Station(result.View, ManufactureStations.Lmd);
        Assert.Equal(2, lmd.Level);
        Assert.Equal(ManufactureRules.LevelOf(ManufactureStations.Lmd, 2), lmd.Current);
        Assert.Equal(3, lmd.Next!.To.Level);
        Assert.Equal(KeyItems.ManufactureBlueprint3, lmd.Next.BlueprintItemId);
        Assert.Equal("2-3", lmd.Next.BlueprintStageCode);
    }

    [Fact]
    public async Task Raising_the_record_station_pays_its_basic_records_and_carries_the_part_made_over()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, Lmd, 8_000);
        int basicBefore = await HeldAsync(player, BasicRecord);

        // Six hours twenty: three Basic records, and twenty minutes into the fourth.
        DateTimeOffset anchor = await AnchorAsync(player, ManufactureStations.Exp, TimeSpan.FromHours(6) + TimeSpan.FromMinutes(20));

        ManufactureActionResult result = await ResultOf(await UpgradeAsync(player, ManufactureStations.Exp, 1));

        Assert.Equal([new ItemStack(BasicRecord, 3)], result.Granted);
        Assert.Equal(basicBefore + 3, Amount(result.Player, BasicRecord));
        Assert.Equal(0, Amount(result.Player, Lmd));

        // Level 2 makes Frontline records every 3 hours; the 20 minutes already made carry over.
        StationState exp = Station(result.View, ManufactureStations.Exp);
        Assert.Equal(2, exp.Level);
        Assert.Equal(FrontlineRecord, exp.Current.ItemId);
        Assert.Equal(0, exp.StoredUnits);
        Assert.Equal(anchor.AddHours(6), exp.AnchorAt);
        Assert.Equal(anchor.AddHours(9), exp.NextUnitAt);
    }

    [Fact]
    public async Task A_screen_that_fell_behind_cannot_raise_a_station_a_second_time()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, KeyItems.ManufactureBlueprint3, 1);
        await GiveAsync(player, Lmd, 100_000);

        await ResultOf(await UpgradeAsync(player, ManufactureStations.Lmd, 1));
        HttpResponseMessage stale = await UpgradeAsync(player, ManufactureStations.Lmd, 1);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(ErrorCodes.StateConflict, (await ProblemOf(stale)).Code);
        Assert.Equal(2, Station(await ViewAsync(player), ManufactureStations.Lmd).Level);
        Assert.Equal(92_000, await HeldAsync(player, Lmd));
    }

    [Fact]
    public async Task Two_upgrades_at_once_charge_once()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, Lmd, 20_000);

        HttpResponseMessage[] responses = await Task.WhenAll(
            UpgradeAsync(player, ManufactureStations.Lmd, 1),
            UpgradeAsync(player, ManufactureStations.Lmd, 1));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(12_000, await HeldAsync(player, Lmd));
        Assert.Equal(2, Station(await ViewAsync(player), ManufactureStations.Lmd).Level);
    }

    [Fact]
    public async Task Collecting_everything_while_a_station_is_raised_settles_both_without_deadlock()
    {
        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, Lmd, 20_000);
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(95));
        await AnchorAsync(player, ManufactureStations.Exp, TimeSpan.FromHours(5));
        int basicBefore = await HeldAsync(player, BasicRecord);

        HttpResponseMessage[] responses = await Task.WhenAll(
            CollectAsync(player, null),
            UpgradeAsync(player, ManufactureStations.Exp, 1));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Whichever went first, each unit was paid exactly once: 3 LMD units and 2 Basic records.
        Assert.Equal(20_000 - 8_000 + 150, await HeldAsync(player, Lmd));
        Assert.Equal(basicBefore + 2, await HeldAsync(player, BasicRecord));
        Assert.Equal(2, Station(await ViewAsync(player), ManufactureStations.Exp).Level);
    }

    [Fact]
    public async Task The_top_level_cannot_be_raised()
    {
        AuthSession player = await UnlockedAsync();
        await SetLevelAsync(player, ManufactureStations.Lmd, 3);

        HttpResponseMessage response = await UpgradeAsync(player, ManufactureStations.Lmd, 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.LevelCapped, (await ProblemOf(response)).Code);
    }

    [Fact]
    public async Task A_level_the_table_does_not_have_plays_as_the_nearest_one()
    {
        AuthSession player = await UnlockedAsync();
        await SetLevelAsync(player, ManufactureStations.Exp, 7);

        StationState exp = Station(await ViewAsync(player), ManufactureStations.Exp);

        Assert.Equal(3, exp.Level);
        Assert.Null(exp.Next);
    }

    // ------------------------------------------------------------- blueprints

    [Fact]
    public async Task A_player_who_cleared_the_stages_before_manufacture_is_handed_their_blueprints_on_opening_it()
    {
        AuthSession player = await GuestAsync();
        await ClearedAsync(player, "main_1_1");
        await ClearedAsync(player, "main_1_3");

        ManufactureView first = await ViewAsync(player);

        // Opened on this very read, with the save that changed under it.
        Assert.True(first.IsUnlocked);
        Assert.NotNull(first.Player);
        Assert.Equal(1, Amount(first.Player!, KeyItems.ManufactureBlueprint1));
        Assert.Equal(1, Amount(first.Player!, KeyItems.ManufactureBlueprint2));
        Assert.Equal(0, Amount(first.Player!, KeyItems.ManufactureBlueprint3));

        // Nothing more is owed, so the next read hands out nothing and carries no save.
        ManufactureView again = await ViewAsync(player);
        Assert.Null(again.Player);
        Assert.Equal(1, await HeldAsync(player, KeyItems.ManufactureBlueprint1));
        Assert.Equal(1, await HeldAsync(player, KeyItems.ManufactureBlueprint2));
    }

    [Fact]
    public async Task A_blueprint_already_held_is_never_handed_out_a_second_time()
    {
        AuthSession player = await GuestAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint1, 1);
        await ClearedAsync(player, "main_1_1");

        ManufactureView view = await ViewAsync(player);

        Assert.Null(view.Player);
        Assert.Equal(1, await HeldAsync(player, KeyItems.ManufactureBlueprint1));
    }

    [Fact]
    public async Task A_key_item_is_never_spent_by_anything()
    {
        AuthSession player = await UnlockedAsync();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
            var inventory = scope.ServiceProvider.GetRequiredService<Inventory>();
            await using var transaction = await db.Database.BeginTransactionAsync();

            Assert.False(await inventory.TryDebitAsync(player.PlayerId, KeyItems.ManufactureBlueprint1, 1, CancellationToken.None));
            await transaction.CommitAsync();
        }

        Assert.Equal(1, await HeldAsync(player, KeyItems.ManufactureBlueprint1));
    }

    [Fact]
    public async Task The_backfill_is_counted_as_such()
    {
        using var granted = new MetricCollector<long>(
            factory.Services.GetRequiredService<IMeterFactory>(), PromuseMetrics.MeterName, "promuse.manufacture.blueprints_granted");

        AuthSession player = await GuestAsync();
        await ClearedAsync(player, "main_1_1");
        await ViewAsync(player);
        await ViewAsync(player);

        CollectedMeasurement<long> given = Assert.Single(granted.GetMeasurementSnapshot());
        Assert.Equal((1L, (object?)KeyItems.ManufactureBlueprint1, "backfill"), (given.Value, given.Tags["item"], given.Tags["path"]));
    }

    // --------------------------------------------------------------- refusals

    [Fact]
    public async Task Unknown_stations_and_levels_are_refused_as_malformed()
    {
        AuthSession player = await UnlockedAsync();

        foreach ((HttpResponseMessage response, string field) in new[]
                 {
                     (await CollectAsync(player, "trading_post"), "stationId"),
                     (await UpgradeAsync(player, "LMD", 1), "stationId"),
                     (await UpgradeAsync(player, ManufactureStations.Lmd, 0), "fromLevel"),
                 })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            ApiProblem problem = await ProblemOf(response);
            Assert.Equal(ErrorCodes.ValidationFailed, problem.Code);
            Assert.True(problem.Errors!.ContainsKey(field), field);
        }
    }

    [Fact]
    public async Task Switched_off_manufacture_refuses_everything_and_creates_nothing()
    {
        AuthSession player = await GuestAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint1, 1);
        await SwitchAsync(on: false);

        foreach (HttpResponseMessage refused in new[]
                 {
                     await _client.SendAsync(Send(HttpMethod.Get, "/v1/manufacture", player)),
                     await CollectAsync(player, null),
                     await UpgradeAsync(player, ManufactureStations.Lmd, 1),
                 })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Equal(ErrorCodes.FeatureDisabled, (await ProblemOf(refused)).Code);
        }

        Assert.Equal(0, await StationRowsAsync(player));
    }

    [Fact]
    public void A_config_written_before_manufacture_existed_reads_it_as_off()
    {
        var store = factory.Services.GetRequiredService<RemoteConfigStore>();

        Assert.False(store.Parse(PromuseDbContext.DefaultConfigDocument).Features.Manufacture);
        Assert.True(store.Parse(PromuseDbContext.DefaultConfigDocument.Replace(
            "\"leaderboards\":true", "\"leaderboards\":true,\"manufacture\":true")).Features.Manufacture);
    }

    [Fact]
    public async Task Signed_out_is_refused()
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/manufacture", session: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------- metrics

    [Fact]
    public async Task Collecting_and_raising_are_counted_and_a_refusal_is_not()
    {
        using var collected = new MetricCollector<long>(
            factory.Services.GetRequiredService<IMeterFactory>(), PromuseMetrics.MeterName, "promuse.manufacture.collected");
        using var upgrades = new MetricCollector<long>(
            factory.Services.GetRequiredService<IMeterFactory>(), PromuseMetrics.MeterName, "promuse.manufacture.upgrades");

        AuthSession player = await UnlockedAsync();
        await GiveAsync(player, KeyItems.ManufactureBlueprint2, 1);
        await GiveAsync(player, Lmd, 8_000);
        await AnchorAsync(player, ManufactureStations.Lmd, TimeSpan.FromMinutes(31));

        await ResultOf(await CollectAsync(player, ManufactureStations.Lmd));
        await ResultOf(await UpgradeAsync(player, ManufactureStations.Lmd, 1));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await UpgradeAsync(player, ManufactureStations.Lmd, 1)).StatusCode);

        CollectedMeasurement<long> made = Assert.Single(collected.GetMeasurementSnapshot());
        Assert.Equal((50L, "lmd", (object?)Lmd), (made.Value, made.Tags["station"], made.Tags["item"]));

        CollectedMeasurement<long> raised = Assert.Single(upgrades.GetMeasurementSnapshot());
        Assert.Equal((1L, "lmd", (object?)2), (raised.Value, raised.Tags["station"], raised.Tags["to_level"]));
    }
}
