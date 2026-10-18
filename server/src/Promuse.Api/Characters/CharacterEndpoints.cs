using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Characters;

namespace Promuse.Api.Characters;

public static class CharacterEndpoints
{
    public static void MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/characters")
            .WithTags("characters")
            .RequireAuthorization();

        group.MapPost("/{characterId}/level-up", LevelUpAsync)
             // 必须幂等 / Spends records and LMD: a retry after a lost answer must replay the
             // first one rather than levelling - and charging - twice.
             .AddEndpointFilter<IdempotencyFilter<LevelUpRequest>>();

        group.MapPost("/{characterId}/promote", PromoteAsync)
             // 同理 / Spends LMD and materials, so the same holds.
             .AddEndpointFilter<IdempotencyFilter<PromoteRequest>>();
    }

    private static async Task<IResult> PromoteAsync(
        string characterId, PromoteRequest request, ClaimsPrincipal user,
        CharacterService characters, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await characters.PromoteAsync(accountId, characterId, request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> LevelUpAsync(
        string characterId, LevelUpRequest request, ClaimsPrincipal user,
        CharacterService characters, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await characters.LevelUpAsync(accountId, characterId, request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
    }
}
