using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.Infrastructure;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Tests;

/// <summary>
/// The idempotency filter's own save: once the handler has answered, the filter writes its record
/// and nothing else.
///
/// 为什么不走端点 / Around the filter itself, with the invocation built by hand, rather than through
/// an endpoint - because no endpoint can show the fault any more. It takes a handler that changes a
/// row it loaded tracked and then refuses: the Manufacture upgrade short of LMD did exactly that, and
/// the filter's save moved the station's anchor after the transaction that took the LMD back had
/// rolled back. Every service behind the filter now leaves its rows alone until nothing can refuse,
/// so through any of them this test would pass with the filter still writing every tracked change,
/// and prove nothing about the filter. Here the handler breaks that rule on purpose.
///
/// 其余都是真的 / Everything around it is what a request gets: the API's own scope, DbContext and
/// JSON options, on the real Postgres - the gate is an advisory lock there and the record a row under
/// a unique index. Only routing and binding are left out; the body is handed over the way binding
/// hands it.
/// </summary>
[Collection(ApiCollection.Name)]
public class IdempotencyFilterTests(PromuseApiFactory factory)
{
    private const string Url = "/v1/tests/idempotency";
    private const int Orirock = 6;

    /// <summary>Stands in for whatever an endpoint binds; the filter hashes it.</summary>
    public sealed record Body(string Note);

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    // --------------------------------------------------------------- helpers

    private async Task<AuthSession> GuestAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/guest")
        {
            Content = JsonContent.Create(new GuestSignInRequest(Unique)),
        };
        request.Headers.Add(IdempotencyFilter<Body>.HeaderName, Unique);

        HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSession>())!;
    }

    /// <summary>
    /// What routing and binding hand the filter for a POST from <paramref name="player"/> under
    /// <paramref name="key"/>, with <paramref name="scope"/> as the request's services.
    /// </summary>
    private static EndpointFilterInvocationContext Invocation(IServiceScope scope, AuthSession player, string key, Body body)
    {
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = Url;
        http.Request.Headers[IdempotencyFilter<Body>.HeaderName] = key;
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", player.PlayerId.ToString())], "Bearer"));

        return new DefaultEndpointFilterInvocationContext(http, body);
    }

    private static int? StatusOf(object? result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static ApiProblem ProblemOf(object? result) =>
        Assert.IsType<ApiProblem>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    private async Task<int> StaminaAsync(AuthSession player)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        return await db.Players.Where(p => p.AccountId == player.PlayerId).Select(p => p.Stamina).SingleAsync();
    }

    private async Task<bool> HoldsAsync(AuthSession player, int itemId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        return await db.PlayerItems.AnyAsync(i => i.AccountId == player.PlayerId && i.ItemId == itemId);
    }

    private async Task<List<IdempotencyRecord>> RecordsAsync(string key)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
        return await db.IdempotencyRecords.AsNoTracking().Where(r => r.Key == key).ToListAsync();
    }

    // ----------------------------------------------------------------- tests

    [Fact]
    public async Task A_refused_request_is_stored_but_nothing_its_handler_left_tracked_is_written()
    {
        AuthSession player = await GuestAsync();
        int stamina = await StaminaAsync(player);
        string key = Unique;

        using IServiceScope request = factory.Services.CreateScope();

        object? answer = await new IdempotencyFilter<Body>().InvokeAsync(
            Invocation(request, player, key, new Body("short of LMD")),
            async context =>
            {
                // 故意违反 / What no service may do: change a row it loaded and add another, then refuse.
                var db = context.HttpContext.RequestServices.GetRequiredService<PromuseDbContext>();

                Player row = await db.Players.SingleAsync(p => p.AccountId == player.PlayerId);
                row.Stamina = 0;
                db.PlayerItems.Add(new PlayerItem { AccountId = player.PlayerId, ItemId = Orirock, Amount = 30 });

                return ApiProblems.InsufficientFunds().ToResult();
            });

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(answer));

        // Neither the change nor the new row reached the database...
        Assert.Equal(stamina, await StaminaAsync(player));
        Assert.False(await HoldsAsync(player, Orirock));

        // ...but the refusal did: it is the key's answer, for a retry to be given.
        IdempotencyRecord record = Assert.Single(await RecordsAsync(key));
        Assert.Equal(StatusCodes.Status409Conflict, record.StatusCode);
        Assert.Contains(ErrorCodes.InsufficientFunds, record.ResponseBody);
    }

    /// <summary>
    /// 唯一键兜底 / The catch behind the gate, which no request through this filter reaches: the gate
    /// queues two of them on one key, so the second finds the first one's record before it runs. A
    /// record that got in some other way while the handler ran is the key's answer all the same - the
    /// unique index refuses the filter's own, and the filter replays the one already there. Here that
    /// one is another request's, so this request is told the key is taken.
    /// </summary>
    [Fact]
    public async Task A_record_that_got_in_first_past_the_gate_answers_for_the_key()
    {
        AuthSession player = await GuestAsync();
        string key = Unique;

        using IServiceScope request = factory.Services.CreateScope();

        object? answer = await new IdempotencyFilter<Body>().InvokeAsync(
            Invocation(request, player, key, new Body("second")),
            async _ =>
            {
                // 另一个写入者 / Another writer, on a connection of its own, storing an answer for this
                // key on this endpoint - named the way the filter names it: whose, the method, the path.
                using IServiceScope other = factory.Services.CreateScope();
                var db = other.ServiceProvider.GetRequiredService<PromuseDbContext>();

                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    Id = Guid.NewGuid(),
                    Key = key,
                    Endpoint = $"{player.PlayerId} {HttpMethods.Post} {Url}",
                    RequestHash = "another request",
                    StatusCode = StatusCodes.Status201Created,
                    ResponseBody = "{}",
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
                });
                await db.SaveChangesAsync();

                return Results.Json(new { handled = true }, statusCode: StatusCodes.Status200OK);
            });

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(answer));
        Assert.Equal(ErrorCodes.IdempotencyKeyConflict, ProblemOf(answer).Code);

        IdempotencyRecord stored = Assert.Single(await RecordsAsync(key));
        Assert.Equal("another request", stored.RequestHash);
    }
}
