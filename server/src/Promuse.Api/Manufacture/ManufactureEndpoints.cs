using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Manufacture;

namespace Promuse.Api.Manufacture;

public static class ManufactureEndpoints
{
    /// <summary>What a switched-off Manufacture is called when it refuses.</summary>
    private const string Feature = "Manufacture";

    public static void MapManufactureEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/manufacture")
            .WithTags("manufacture")
            .RequireAuthorization();

        // 读也守 / Reading is behind the flag too: it creates a player's stations the first time,
        // so a switched-off Manufacture changes nothing at all.
        group.MapGet("", ViewAsync)
             .RequireFeature(f => f.Manufacture, Feature);

        group.MapPost("/collect", CollectAsync)
             .RequireFeature(f => f.Manufacture, Feature)
             // 必须幂等 / Pays out: a retry after a lost answer must replay the first one rather
             // than collect again - which would find nothing, and tell the player so.
             .AddEndpointFilter<IdempotencyFilter<CollectRequest>>();

        group.MapPost("/upgrade", UpgradeAsync)
             .RequireFeature(f => f.Manufacture, Feature)
             // 同理 / Spends LMD, so the same holds.
             .AddEndpointFilter<IdempotencyFilter<UpgradeRequest>>();
    }

    private static async Task<IResult> ViewAsync(ClaimsPrincipal user, ManufactureService manufacture, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();
        return Results.Json(await manufacture.ViewAsync(accountId, ct));
    }

    private static async Task<IResult> CollectAsync(
        CollectRequest request, ClaimsPrincipal user, ManufactureService manufacture, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await manufacture.CollectAsync(accountId, request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> UpgradeAsync(
        UpgradeRequest request, ClaimsPrincipal user, ManufactureService manufacture, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await manufacture.UpgradeAsync(accountId, request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }
}
