using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Api.Players;
using Promuse.Contracts.Campaign;

namespace Promuse.Api.Campaign;

public static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/campaign", ViewAsync)
           .WithTags("campaign")
           .RequireAuthorization();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/campaign")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", AdminGetAsync);
        admin.MapPut("", SaveAsync);
    }

    private static async Task<IResult> ViewAsync(ClaimsPrincipal user, CampaignService campaign, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();
        return Results.Json(await campaign.ViewAsync(accountId, ct));
    }

    private static async Task<IResult> AdminGetAsync(HttpContext http, CampaignService campaign, CancellationToken ct)
    {
        AdminCampaign current = await campaign.AdminGetAsync(ct);
        http.Response.Headers.ETag = PlayerService.ETagFor(current.Version);
        return Results.Json(current);
    }

    private static async Task<IResult> SaveAsync(CampaignUpsertRequest request, HttpContext http, ClaimsPrincipal user,
                                                 CampaignService campaign, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await campaign.SaveAsync(adminId, http.Request.Headers[HeaderNames.IfMatch], request, ct);
        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        http.Response.Headers.ETag = PlayerService.ETagFor(outcome.Value!.Version);
        return Results.Json(outcome.Value);
    }
}
