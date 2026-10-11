using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Api.Players;
using Promuse.Contracts.Notices;

namespace Promuse.Api.Notices;

public static class NoticeEndpoints
{
    public static void MapNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/notices")
            .WithTags("notices")
            .RequireAuthorization();

        group.MapGet("", ListAsync);
        group.MapPost("/{noticeId:guid}/read", ReadAsync);

        // 图片不用登录 / Banners are public pictures with unguessable names, served without a token
        // so the game can hand the URL straight to its image loader.
        app.MapGet(NoticeService.BannerRoute + "{name}", Banner)
           .WithTags("notices")
           .AllowAnonymous();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/notices")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", AdminListAsync);
        admin.MapPost("", CreateAsync);
        admin.MapPut("/{noticeId:guid}", UpdateAsync);
        admin.MapPost("/banners", UploadAsync).DisableAntiforgery();
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, NoticeService notices, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();
        return Results.Json(await notices.ListAsync(accountId, ct));
    }

    private static async Task<IResult> ReadAsync(Guid noticeId, ClaimsPrincipal user, NoticeService notices, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await notices.ReadAsync(accountId, noticeId, ct);
        return outcome.IsSuccess ? Results.NoContent() : outcome.Problem!.ToResult();
    }

    private static IResult Banner(string name, NoticeService notices, HttpContext http)
    {
        if (notices.Banner(name) is not { } file) return Results.NotFound();

        // 内容决定名字 / The name is the content's hash, so a cached copy can never be stale.
        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.File(file.Path, file.ContentType);
    }

    private static async Task<IResult> AdminListAsync(NoticeService notices, CancellationToken ct) =>
        Results.Json(await notices.AdminListAsync(ct));

    private static Task<IResult> CreateAsync(NoticeUpsertRequest request, HttpContext http, ClaimsPrincipal user,
                                             NoticeService notices, CancellationToken ct) =>
        SaveAsync(null, request, http, user, notices, ct);

    private static Task<IResult> UpdateAsync(Guid noticeId, NoticeUpsertRequest request, HttpContext http, ClaimsPrincipal user,
                                             NoticeService notices, CancellationToken ct) =>
        SaveAsync(noticeId, request, http, user, notices, ct);

    private static async Task<IResult> SaveAsync(Guid? noticeId, NoticeUpsertRequest request, HttpContext http,
                                                 ClaimsPrincipal user, NoticeService notices, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await notices.SaveAsync(adminId, noticeId, http.Request.Headers[HeaderNames.IfMatch], request?.Definition, ct);
        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        http.Response.Headers.ETag = PlayerService.ETagFor(outcome.Value!.Version);
        return Results.Json(outcome.Value, statusCode: outcome.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, NoticeService notices, CancellationToken ct)
    {
        IFormFile? file = request.HasFormContentType ? (await request.ReadFormAsync(ct)).Files.GetFile("file") : null;

        var outcome = await notices.UploadAsync(file, ct);
        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }
}
