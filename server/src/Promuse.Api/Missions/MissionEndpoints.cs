using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Missions;

namespace Promuse.Api.Missions;

public static class MissionEndpoints
{
    public static void MapMissionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/missions")
            .WithTags("missions")
            .RequireAuthorization();

        group.MapGet("/", GetBoardsAsync);

        // 领取都要幂等键 / Both claims take an Idempotency-Key. Each one consumes
        // something that cannot be consumed twice, and a reward claim also hands
        // over items - so a retry after a lost response has to replay the answer
        // rather than be told it is too late.
        group.MapPost("/claims", ClaimMissionAsync)
             .AddEndpointFilter<IdempotencyFilter<ClaimMissionRequest>>();

        group.MapPost("/reward-claims", ClaimRewardAsync)
             .AddEndpointFilter<IdempotencyFilter<ClaimRewardRequest>>();
    }

    private static async Task<IResult> GetBoardsAsync(
        ClaimsPrincipal user, MissionService missions, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await missions.GetBoardsAsync(accountId, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> ClaimMissionAsync(
        ClaimMissionRequest request, ClaimsPrincipal user,
        MissionService missions, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await missions.ClaimMissionAsync(accountId, request.Tab, request.MissionId, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> ClaimRewardAsync(
        ClaimRewardRequest request, ClaimsPrincipal user,
        MissionService missions, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await missions.ClaimRewardAsync(accountId, request.Tab, request.RewardId, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }
}
