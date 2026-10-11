using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Api.Players;
using Promuse.Contracts.Stories;

namespace Promuse.Api.Stories;

public static class StoryEndpoints
{
    public static void MapStoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/stories/patches", ListAsync)
           .WithTags("stories")
           .RequireAuthorization();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/stories/patches")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", AdminListAsync);
        admin.MapPut("/{storyId}/{lineId}", SaveAsync);
        admin.MapDelete("/{storyId}/{lineId}", RevertAsync);
    }

    /// <summary>
    /// Every correction in force, with the revision as the ETag: a game that already holds this
    /// revision gets 304 and no body, so asking before every story costs one empty round trip.
    /// </summary>
    private static async Task<IResult> ListAsync(HttpContext http, StoryPatchService stories, CancellationToken ct)
    {
        int revision = await stories.RevisionAsync(ct);
        http.Response.Headers.ETag = PlayerService.ETagFor(revision);

        if (PlayerService.TryParseETag(http.Request.Headers.IfNoneMatch, out int seen) && seen == revision)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        StoryPatchList list = await stories.ListAsync(ct);
        http.Response.Headers.ETag = PlayerService.ETagFor(list.Revision);
        return Results.Json(list);
    }

    private static async Task<IResult> AdminListAsync(StoryPatchService stories, CancellationToken ct) =>
        Results.Json(await stories.AdminListAsync(ct));

    private static async Task<IResult> SaveAsync(
        string storyId, string lineId, StoryPatchRequest request, ClaimsPrincipal user,
        StoryPatchService stories, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await stories.SaveAsync(adminId, storyId, lineId, request, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> RevertAsync(
        string storyId, string lineId, ClaimsPrincipal user, StoryPatchService stories, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await stories.RevertAsync(adminId, storyId, lineId, ct);
        return outcome.IsSuccess ? Results.NoContent() : outcome.Problem!.ToResult();
    }
}
