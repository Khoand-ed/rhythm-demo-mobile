using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts.Config;

namespace Promuse.Api.LiveOps;

public static class RemoteConfigEndpoints
{
    public static void MapRemoteConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // 不用登录 / Anonymous: a client has to learn about maintenance or a required update
        // before it can sign in, and there is nothing in here that belongs to anyone.
        app.MapGet("/v1/config", GetAsync)
           .WithTags("config")
           .AllowAnonymous();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/config")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("/history", HistoryAsync);
        admin.MapPut("", UpdateAsync);
        admin.MapPost("/rollback", RollbackAsync);

        // 管理页 / The admin page. Only a page: everything it does goes through the admin API
        // above, which checks the caller on every request, so serving it to anyone gives away
        // nothing but a login form.
        //
        // One route answers /admin and /admin/ alike - routing ignores a trailing slash, and a
        // second route for it is an ambiguous match, not a redirect.
        app.MapGet("/admin", () => Results.Content(AdminPage.Value, "text/html; charset=utf-8"))
           .ExcludeFromDescription();
    }

    private static readonly Lazy<string> AdminPage = new(() =>
    {
        using Stream stream = typeof(RemoteConfigEndpoints).Assembly
            .GetManifestResourceStream("Promuse.Api.LiveOps.admin.html")
            ?? throw new InvalidOperationException("The admin page was not embedded in the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>
    /// The live config, with its version as the ETag. A client holding the current version gets
    /// 304 and no body, so checking on every return to Home costs one empty round trip.
    /// </summary>
    private static async Task<IResult> GetAsync(HttpContext http, RemoteConfigStore store, TimeProvider clock, CancellationToken ct)
    {
        RemoteConfigStore.Snapshot config = await store.GetAsync(ct);

        http.Response.Headers.ETag = PlayerService.ETagFor(config.Version);

        if (PlayerService.TryParseETag(http.Request.Headers.IfNoneMatch, out int seen) && seen == config.Version)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Json(new RemoteConfig(config.Version, config.Document, config.UpdatedAt, clock.GetUtcNow()));
    }

    private static async Task<IResult> HistoryAsync(RemoteConfigService service, CancellationToken ct, int limit = 20)
    {
        var outcome = await service.GetHistoryAsync(limit, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static Task<IResult> UpdateAsync(
        ConfigUpdateRequest request, HttpContext http, ClaimsPrincipal user, RemoteConfigService service, CancellationToken ct) =>
        WriteAsync(http, user, (adminId, ifMatch) => service.UpdateAsync(adminId, ifMatch, request, ct));

    private static Task<IResult> RollbackAsync(
        ConfigRollbackRequest request, HttpContext http, ClaimsPrincipal user, RemoteConfigService service, CancellationToken ct) =>
        WriteAsync(http, user, (adminId, ifMatch) => service.RollbackAsync(adminId, ifMatch, request, ct));

    private static async Task<IResult> WriteAsync(
        HttpContext http, ClaimsPrincipal user, Func<Guid, string?, Task<Outcome<RemoteConfig>>> write)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await write(adminId, http.Request.Headers[HeaderNames.IfMatch]);
        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        http.Response.Headers.ETag = PlayerService.ETagFor(outcome.Value!.Version);
        return Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created);
    }
}
