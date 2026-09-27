using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Players;

namespace Promuse.Api.Players;

public static class PlayerEndpoints
{
    public static void MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/players")
            .WithTags("players")
            .RequireAuthorization();

        group.MapGet("/me", GetMeAsync);
        group.MapPut("/me/squad", SetSquadAsync);
        group.MapPut("/me/desktop-character", SetDesktopCharacterAsync);
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext http, ClaimsPrincipal user, PlayerService players, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await players.GetStateAsync(accountId, ct);

        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        PlayerState state = outcome.Value!;
        string etag = PlayerService.ETagFor(state.StateVersion);

        http.Response.Headers.ETag = etag;

        // 304 只说"没人写过" / A 304 here means no write has landed, not that the
        // response would be identical - the stamina bar has kept filling. That is
        // deliberate and safe, because the last full response carried nextPointAt
        // and fullAt for exactly this reason: the client counts down locally
        // rather than polling for a number it can already work out.
        if (PlayerService.TryParseETag(http.Request.Headers.IfNoneMatch, out int seen) &&
            seen == state.StateVersion)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Json(state, statusCode: StatusCodes.Status200OK);
    }

    private static Task<IResult> SetSquadAsync(
        SquadRequest request, HttpContext http, ClaimsPrincipal user,
        PlayerService players, CancellationToken ct) =>
        WriteAsync(http, user, ct, (accountId, ifMatch) =>
            players.SetSquadAsync(accountId, request.Squad, ifMatch, ct));

    private static Task<IResult> SetDesktopCharacterAsync(
        DesktopCharacterRequest request, HttpContext http, ClaimsPrincipal user,
        PlayerService players, CancellationToken ct) =>
        WriteAsync(http, user, ct, (accountId, ifMatch) =>
            players.SetDesktopCharacterAsync(accountId, request.CharacterId, ifMatch, ct));

    /// <summary>
    /// The half both writes share: identify the caller, carry If-Match through,
    /// and answer with the whole state plus its new ETag.
    ///
    /// 写完回整个状态 / Returning the full document rather than 204 is a choice:
    /// the client then never has to guess what the write did, and never holds a
    /// version number it has not seen the body for.
    /// </summary>
    private static async Task<IResult> WriteAsync(
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken ct,
        Func<Guid, string?, Task<Outcome<PlayerState>>> write)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        string? ifMatch = http.Request.Headers[HeaderNames.IfMatch];

        var outcome = await write(accountId, ifMatch);

        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        PlayerState state = outcome.Value!;

        http.Response.Headers.ETag = PlayerService.ETagFor(state.StateVersion);

        return Results.Json(state, statusCode: StatusCodes.Status200OK);
    }
}
