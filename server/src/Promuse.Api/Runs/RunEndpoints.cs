using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Runs;

namespace Promuse.Api.Runs;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/runs")
            .WithTags("runs")
            .RequireAuthorization();

        group.MapPost("/", StartAsync)
             // 开一局要花理智 / Starting a run spends stamina, so a retry that
             // reached the server the first time must not charge a second bar.
             .AddEndpointFilter<IdempotencyFilter<StartRunRequest>>();

        group.MapPost("/{runId:guid}/complete", CompleteAsync)
             .AddEndpointFilter<IdempotencyFilter<CompleteRunRequest>>()
             // 带着 trace / The body carries the input trace. Capped well above the largest real
             // one, and far below Kestrel's 30 MB default - this is the endpoint that accepts the
             // most bytes from a client, so it is the one that gets an explicit limit.
             .WithMetadata(new RequestSizeLimitAttribute(4 * 1024 * 1024));
    }

    /// <summary>
    /// Closes a run: reviews its result, counts it towards the missions if it is a clear, and
    /// ranks it if it was accepted. One call for "the run ended", so a run cannot be counted
    /// by one request and scored by another.
    /// </summary>
    private static async Task<IResult> CompleteAsync(
        Guid runId, CompleteRunRequest request, ClaimsPrincipal user,
        RunService runs, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await runs.CompleteAsync(accountId, runId, request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> StartAsync(
        StartRunRequest request, ClaimsPrincipal user, RunService runs, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await runs.StartAsync(accountId, request.StageId, request.CharacterId, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }
}
