using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.SignIn;
using Empty = Promuse.Api.Events.EventEndpoints.Empty;

namespace Promuse.Api.SignIn;

public static class SignInEndpoints
{
    public static void MapSignInEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/sign-in")
            .WithTags("sign-in")
            .RequireAuthorization();

        group.MapGet("", MonthAsync);
        group.MapPost("", SignAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/sign-in")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", TablesAsync);
        admin.MapPut("/{key}", SaveAsync);
        admin.MapDelete("/{key}", DeleteAsync);
    }

    private static async Task<IResult> MonthAsync(ClaimsPrincipal user, SignInService signIn, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();
        return Results.Json(await signIn.MonthAsync(accountId, ct));
    }

    private static async Task<IResult> SignAsync(ClaimsPrincipal user, SignInService signIn, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await signIn.SignAsync(accountId, ct);
        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> TablesAsync(SignInService signIn, CancellationToken ct) =>
        Results.Json(await signIn.TablesAsync(ct));

    private static async Task<IResult> SaveAsync(string key, SignInTableRequest request, ClaimsPrincipal user,
                                                 SignInService signIn, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await signIn.SaveAsync(adminId, key, request, ct);
        return outcome.IsSuccess ? Results.Json(outcome.Value) : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> DeleteAsync(string key, SignInService signIn, CancellationToken ct)
    {
        var outcome = await signIn.DeleteAsync(key, ct);
        return outcome.IsSuccess ? Results.NoContent() : outcome.Problem!.ToResult();
    }
}
