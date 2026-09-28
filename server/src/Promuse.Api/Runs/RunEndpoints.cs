using System.Security.Claims;
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
             .AddEndpointFilter<IdempotencyFilter<CompleteRunRequest>>();
    }

    /// <summary>
    /// Closes a run. Thin for now - it exists so the mission counters have
    /// something to advance them. Phase 4 adds the score and the input trace to
    /// this same endpoint and replays them against the seed the run was opened
    /// with, rather than introducing a second call that also means "it ended".
    /// </summary>
    private static async Task<IResult> CompleteAsync(
        Guid runId, CompleteRunRequest request, ClaimsPrincipal user,
        RunService runs, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await runs.CompleteAsync(accountId, runId, request.Won, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> StartAsync(
        StartRunRequest request, ClaimsPrincipal user, RunService runs, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await runs.StartAsync(accountId, request.StageId, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }
}
