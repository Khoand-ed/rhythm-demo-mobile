using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Moderation;
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
        group.MapPut("/me/signature", SetSignatureAsync);

        // 起名, 只一次 / The name in the game, chosen once after signing up.
        group.MapPut("/me/name", SetNameAsync);

        group.MapGet("/me/profile", GetMyProfileAsync);
        group.MapGet("/{playerId:guid}/profile", GetProfileAsync);

        // 举报 / Reporting the line on someone else's card, for the moderators' queue.
        group.MapPost("/{playerId:guid}/signature/report", ReportSignatureAsync);
    }

    private static async Task<IResult> ReportSignatureAsync(
        Guid playerId, SignatureReportRequest request, ClaimsPrincipal user,
        SignatureReportService reports, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await reports.ReportAsync(accountId, playerId, request, ct);
        return outcome.IsSuccess ? Results.NoContent() : outcome.Problem!.ToResult();
    }

    private static Task<IResult> GetMyProfileAsync(
        ClaimsPrincipal user, ProfileService profiles, CancellationToken ct) =>
        user.TryGetAccountId(out Guid accountId)
            ? GetProfileAsync(accountId, user, profiles, ct)
            : Task.FromResult(ApiProblems.Unauthorized().ToResult());

    /// <summary>
    /// 别人的名片 / Anyone's card, by the id a leaderboard row already carries. No ETag: a card
    /// is read when it is opened, and its counts move with every run the owner finishes.
    /// </summary>
    private static async Task<IResult> GetProfileAsync(
        Guid playerId, ClaimsPrincipal user, ProfileService profiles, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await profiles.GetAsync(accountId, playerId, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status200OK)
            : outcome.Problem!.ToResult();
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

    private static Task<IResult> SetSignatureAsync(
        SignatureRequest request, HttpContext http, ClaimsPrincipal user,
        PlayerService players, CancellationToken ct) =>
        WriteAsync(http, user, ct, (accountId, ifMatch) =>
            players.SetSignatureAsync(accountId, request.Signature, ifMatch, ct));

    private static Task<IResult> SetNameAsync(
        DisplayNameRequest request, HttpContext http, ClaimsPrincipal user,
        PlayerService players, CancellationToken ct) =>
        WriteAsync(http, user, ct, (accountId, ifMatch) =>
            players.SetNameAsync(accountId, request.DisplayName, ifMatch, ct));

    /// <summary>
    /// The half every write shares: identify the caller, carry If-Match through,
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
