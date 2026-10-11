using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Shop;

namespace Promuse.Api.Economy;

public static class EconomyEndpoints
{
    public static void MapEconomyEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/shop")
            .WithTags("shop")
            .RequireAuthorization();

        group.MapGet("/offers", GetOffersAsync);

        group.MapPost("/purchases", PurchaseAsync)
             .RequireFeature(f => f.Shop, "The Store")
             // 必须幂等 / A purchase both takes and gives, so a retry after a lost
             // response must replay the first answer rather than charging twice.
             // This is the case the header exists for.
             .AddEndpointFilter<IdempotencyFilter<PurchaseRequest>>();
    }

    private static async Task<IResult> GetOffersAsync(
        EconomyService economy, CancellationToken ct)
    {
        var outcome = await economy.GetCatalogAsync(ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> PurchaseAsync(
        PurchaseRequest request, ClaimsPrincipal user, EconomyService economy, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await economy.PurchaseAsync(accountId, request.OfferId, request.Quantity, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }
}
