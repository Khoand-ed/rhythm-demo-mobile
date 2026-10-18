using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Moderation;

namespace Promuse.Api.Runs;

/// <summary>The admin page's Runs tab: flagged results waiting for a decision. Administrators only.</summary>
public static class FlaggedRunEndpoints
{
    public static void MapFlaggedRunEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder admin = app.MapGroup("/v1/admin/runs")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("/flagged", ListAsync);
        admin.MapPost("/{runId:guid}/approve", ApproveAsync);
        admin.MapPost("/{runId:guid}/reject", RejectAsync);
    }

    private static async Task<IResult> ListAsync(bool? decided, int? limit, FlaggedRunService runs, CancellationToken ct)
    {
        var outcome = await runs.ListAsync(decided ?? false, limit ?? FlaggedRunService.DefaultLimit, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> ApproveAsync(
        Guid runId, RunDecisionRequest? request, ClaimsPrincipal user, FlaggedRunService runs, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid moderatorId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await runs.ApproveAsync(moderatorId, runId, request, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> RejectAsync(
        Guid runId, RunDecisionRequest? request, ClaimsPrincipal user, FlaggedRunService runs, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid moderatorId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await runs.RejectAsync(moderatorId, runId, request, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }
}
