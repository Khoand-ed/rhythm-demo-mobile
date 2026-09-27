using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Runs;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

[Collection(ApiCollection.Name)]
public class RunEndpointTests(PromuseApiFactory factory)
{
    private const string Stage = "stage_001";
    private const int StageCost = 6;

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

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

    private HttpRequestMessage Start(AuthSession session, string stageId, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/runs")
        {
            Content = JsonContent.Create(new StartRunRequest(stageId)),
        };

        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        request.Headers.Add("Idempotency-Key", key ?? Unique);

        return request;
    }

    /// <summary>Sets the stored bar directly, bypassing the hours of waiting.</summary>
    private async Task SetStaminaAsync(Guid accountId, int amount)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        await db.Players
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Stamina, amount)
                .SetProperty(p => p.StaminaUpdatedAt, DateTimeOffset.UtcNow));
    }

    // ----------------------------------------------------------------- start

    [Fact]
    public async Task Starting_a_run_charges_the_stage_and_issues_a_seed()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Start(session, Stage));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var ticket = (await response.Content.ReadFromJsonAsync<RunTicket>())!;

        Assert.Equal(Stage, ticket.StageId);
        Assert.Equal(StageCost, ticket.StaminaSpent);
        Assert.NotEqual(Guid.Empty, ticket.RunId);

        // 82 at level one, less the cost.
        Assert.Equal(82 - StageCost, ticket.Player.Stamina.Current);

        // Below the cap now, so the bar has a deadline again.
        Assert.NotNull(ticket.Player.Stamina.NextPointAt);
    }

    /// <summary>
    /// 种子必须不可预测 / The seed is the reason this endpoint exists rather than
    /// a bare stamina debit: JudgeUpgradePassive rolls per judgement, so the
    /// server cannot replay a run without knowing the sequence. Two runs must not
    /// share one, or knowing the first tells you the second.
    /// </summary>
    [Fact]
    public async Task Each_run_gets_its_own_seed()
    {
        AuthSession session = await SignInAsync();

        var a = (await (await _client.SendAsync(Start(session, Stage)))
            .Content.ReadFromJsonAsync<RunTicket>())!;
        var b = (await (await _client.SendAsync(Start(session, Stage)))
            .Content.ReadFromJsonAsync<RunTicket>())!;

        Assert.NotEqual(a.RunId, b.RunId);
        Assert.NotEqual(a.Seed, b.Seed);
        Assert.True(a.Seed >= 0 && b.Seed >= 0);
    }

    [Fact]
    public async Task A_run_that_cannot_be_afforded_charges_nothing()
    {
        AuthSession session = await SignInAsync();

        await SetStaminaAsync(session.PlayerId, StageCost - 1);

        HttpResponseMessage response = await _client.SendAsync(Start(session, Stage));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientStamina,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        Assert.Equal(StageCost - 1, await db.Players
            .Where(p => p.AccountId == session.PlayerId).Select(p => p.Stamina).FirstAsync());

        Assert.False(await db.Runs.AnyAsync(r => r.AccountId == session.PlayerId));
    }

    [Fact]
    public async Task An_unknown_stage_is_refused()
    {
        AuthSession session = await SignInAsync();

        HttpResponseMessage response = await _client.SendAsync(Start(session, "stage_does_not_exist"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.StageNotFound,
            (await response.Content.ReadFromJsonAsync<ApiProblem>())!.Code);
    }

    [Fact]
    public async Task The_hard_chart_costs_more()
    {
        AuthSession session = await SignInAsync();

        var ticket = (await (await _client.SendAsync(Start(session, "stage_AIW_hard")))
            .Content.ReadFromJsonAsync<RunTicket>())!;

        Assert.Equal(12, ticket.StaminaSpent);
    }

    /// <summary>
    /// 并发不能扣两次 / Enough stamina for exactly two attempts, five requests at
    /// once. StateVersion is a concurrency token, so the losers change no row
    /// rather than charging a bar that is no longer there.
    /// </summary>
    [Fact]
    public async Task Concurrent_starts_cannot_spend_a_bar_twice()
    {
        AuthSession session = await SignInAsync();

        await SetStaminaAsync(session.PlayerId, StageCost * 2);

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => _client.SendAsync(Start(session, Stage))));

        int created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        int runs = await db.Runs.CountAsync(r => r.AccountId == session.PlayerId);
        int left = await db.Players
            .Where(p => p.AccountId == session.PlayerId).Select(p => p.Stamina).FirstAsync();

        // At most two could succeed, the ledger agrees with the responses, and
        // the bar never goes negative.
        Assert.InRange(created, 1, 2);
        Assert.Equal(created, runs);
        Assert.Equal(StageCost * 2 - created * StageCost, left);
        Assert.True(left >= 0);
    }

    [Fact]
    public async Task Replaying_a_start_key_charges_once()
    {
        AuthSession session = await SignInAsync();
        string key = Unique;

        HttpResponseMessage first = await _client.SendAsync(Start(session, Stage, key));
        HttpResponseMessage replay = await _client.SendAsync(Start(session, Stage, key));

        var a = (await first.Content.ReadFromJsonAsync<RunTicket>())!;
        var b = (await replay.Content.ReadFromJsonAsync<RunTicket>())!;

        // The same ticket, seed included - a replay returns the stored response
        // rather than opening a second run at a second cost.
        Assert.Equal(a.RunId, b.RunId);
        Assert.Equal(a.Seed, b.Seed);

        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        Assert.Equal(1, await db.Runs.CountAsync(r => r.AccountId == session.PlayerId));
        Assert.Equal(82 - StageCost, await db.Players
            .Where(p => p.AccountId == session.PlayerId).Select(p => p.Stamina).FirstAsync());
    }

    [Fact]
    public async Task Starting_without_a_token_is_refused()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/v1/runs", new StartRunRequest(Stage));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
