using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Gacha;

namespace Promuse.Api.Gacha;

public static class GachaEndpoints
{
    public static void MapGachaEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/gacha")
            .WithTags("gacha")
            .RequireAuthorization();

        group.MapGet("/banners", GetBannersAsync);
        group.MapGet("/history", GetHistoryAsync);

        group.MapPost("/pulls", PullAsync)
             // 必须幂等 / A pull both charges and grants, and its result is random. A retry after
             // a lost response must replay the first answer - re-running it would charge twice
             // and, worse, roll again until the player liked the result.
             .AddEndpointFilter<IdempotencyFilter<GachaPullRequest>>();
    }

    private static async Task<IResult> GetBannersAsync(
        ClaimsPrincipal user, GachaService gacha, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await gacha.GetBannersAsync(accountId, ct);

        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> PullAsync(
        GachaPullRequest request, ClaimsPrincipal user, GachaService gacha, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await gacha.PullAsync(accountId, request.BannerId, request.Times, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> GetHistoryAsync(
        ClaimsPrincipal user,
        GachaService gacha,
        CancellationToken ct,
        long? before = null,
        int limit = GachaService.DefaultHistoryPage)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await gacha.GetHistoryAsync(accountId, before, limit, ct);

        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }
}
