using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Leaderboards;

namespace Promuse.Api.Leaderboards;

public static class LeaderboardEndpoints
{
    public static void MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/leaderboards")
            .WithTags("leaderboards")
            .RequireAuthorization();

        group.MapGet("/{stageId}", GetAsync);
    }

    /// <summary>
    /// A stage's board, top first, plus the caller's own line wherever it falls.
    ///
    /// 只读 / Read-only and unconditional: nothing on a leaderboard is written by asking to see
    /// it. Scores reach it only through a run's completion, after review.
    /// </summary>
    private static async Task<IResult> GetAsync(
        string stageId,
        ClaimsPrincipal user,
        LeaderboardService leaderboards,
        CancellationToken ct,
        LeaderboardPeriod period = LeaderboardPeriod.AllTime,
        int limit = LeaderboardService.DefaultLimit)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await leaderboards.GetPageAsync(accountId, stageId, period, limit, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value)
            : outcome.Problem!.ToResult();
    }
}
