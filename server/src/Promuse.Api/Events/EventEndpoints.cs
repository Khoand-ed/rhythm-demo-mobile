using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Api.Players;
using Promuse.Contracts.Events;

namespace Promuse.Api.Events;

public static class EventEndpoints
{
    /// <summary>The body of the claims that carry nothing but the URL - hashed by the idempotency filter.</summary>
    public sealed record Empty;

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/events")
            .WithTags("events")
            .RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapGet("/{eventId}", ViewAsync);

        // 都要幂等 / Every action hands something out, so each is idempotent: a retry after a lost
        // answer replays it instead of checking in twice or buying twice.
        group.MapPost("/{eventId}/check-in", CheckInAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();
        group.MapPost("/{eventId}/missions/{missionId}/claim", ClaimMissionAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();
        group.MapPost("/{eventId}/milestones/{rewardId}/claim", ClaimMilestoneAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();
        group.MapPost("/{eventId}/shop/{offerId}/purchase", PurchaseAsync).AddEndpointFilter<IdempotencyFilter<EventPurchaseRequest>>();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/events")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", AdminListAsync);
        admin.MapGet("/catalog", CatalogAsync);
        admin.MapPut("/{eventId}", UpsertAsync);
    }

    // ----------------------------------------------------------------- player

    private static async Task<IResult> ListAsync(EventService events, CancellationToken ct) =>
        Results.Json(await events.ListAsync(ct));

    private static async Task<IResult> ViewAsync(string eventId, ClaimsPrincipal user, EventService events, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await events.ViewAsync(accountId, eventId, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static Task<IResult> CheckInAsync(string eventId, ClaimsPrincipal user, EventService events, CancellationToken ct) =>
        ActAsync(user, accountId => events.CheckInAsync(accountId, eventId, ct));

    private static Task<IResult> ClaimMissionAsync(
        string eventId, string missionId, ClaimsPrincipal user, EventService events, CancellationToken ct) =>
        ActAsync(user, accountId => events.ClaimMissionAsync(accountId, eventId, missionId, ct));

    private static Task<IResult> ClaimMilestoneAsync(
        string eventId, string rewardId, ClaimsPrincipal user, EventService events, CancellationToken ct) =>
        ActAsync(user, accountId => events.ClaimMilestoneAsync(accountId, eventId, rewardId, ct));

    private static Task<IResult> PurchaseAsync(
        string eventId, string offerId, EventPurchaseRequest request, ClaimsPrincipal user, EventService events, CancellationToken ct) =>
        ActAsync(user, accountId => events.PurchaseAsync(accountId, eventId, offerId, request.Quantity, ct));

    private static async Task<IResult> ActAsync(ClaimsPrincipal user, Func<Guid, Task<Outcome<EventActionResult>>> act)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await act(accountId);
        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }

    // ------------------------------------------------------------------ admin

    private static async Task<IResult> AdminListAsync(EventAdminService admin, CancellationToken ct) =>
        Results.Json(await admin.ListAsync(ct));

    private static async Task<IResult> CatalogAsync(EventAdminService admin, CancellationToken ct) =>
        Results.Json(await admin.CatalogAsync(ct));

    private static async Task<IResult> UpsertAsync(
        string eventId, EventUpsertRequest request, HttpContext http, ClaimsPrincipal user,
        EventAdminService admin, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await admin.UpsertAsync(adminId, eventId, http.Request.Headers[HeaderNames.IfMatch], request, ct);
        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        http.Response.Headers.ETag = PlayerService.ETagFor(outcome.Value!.Version);
        return Results.Json(outcome.Value, statusCode: outcome.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }
}
