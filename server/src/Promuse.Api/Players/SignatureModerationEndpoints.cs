using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Moderation;

namespace Promuse.Api.Players;

/// <summary>The admin page's Signatures tab. Administrators only, like the rest of /v1/admin.</summary>
public static class SignatureModerationEndpoints
{
    public static void MapSignatureModerationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder admin = app.MapGroup("/v1/admin/signatures")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", ListAsync);
        admin.MapGet("/history", HistoryAsync);
        admin.MapPost("/{playerId:guid}/remove", RemoveAsync);
        admin.MapPost("/{playerId:guid}/unlock", UnlockAsync);

        // 举报队列 / The reports players sent; taking a line down above closes its reports too.
        admin.MapGet("/reports", ReportsAsync);
        admin.MapPost("/{playerId:guid}/dismiss-reports", DismissReportsAsync);
    }

    private static async Task<IResult> ReportsAsync(SignatureReportService reports, CancellationToken ct) =>
        Results.Json(await reports.QueueAsync(ct));

    private static async Task<IResult> DismissReportsAsync(
        Guid playerId, SignatureReportDismissRequest? request, ClaimsPrincipal user,
        SignatureReportService reports, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid moderatorId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await reports.DismissAsync(moderatorId, playerId, request, ct);
        return outcome.IsSuccess ? Results.NoContent() : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> ListAsync(
        string? query, int? limit, SignatureModerationService moderation, CancellationToken ct)
    {
        var outcome = await moderation.ListAsync(query, limit ?? SignatureModerationService.DefaultLimit, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> HistoryAsync(
        Guid? playerId, int? limit, SignatureModerationService moderation, CancellationToken ct)
    {
        var outcome = await moderation.HistoryAsync(playerId, limit ?? SignatureModerationService.DefaultLimit, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> RemoveAsync(
        Guid playerId, SignatureRemoveRequest request, ClaimsPrincipal user,
        SignatureModerationService moderation, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid moderatorId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await moderation.RemoveAsync(moderatorId, playerId, request, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> UnlockAsync(
        Guid playerId, SignatureUnlockRequest? request, ClaimsPrincipal user,
        SignatureModerationService moderation, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid moderatorId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await moderation.UnlockAsync(moderatorId, playerId, request, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }
}
