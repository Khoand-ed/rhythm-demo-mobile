using System.Security.Claims;
using System.Text.RegularExpressions;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Auth;

namespace Promuse.Api.Auth;

public static partial class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/auth")
            .WithTags("auth")
            // 登录端点要限流 / Rate limited as a group. Two of these run Argon2,
            // which is expensive by design, so an unlimited login endpoint is a
            // way to spend the server's memory budget for the price of an HTTP
            // request. The cost parameter and this limiter are two halves of one
            // answer.
            .RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/guest", GuestAsync)
             .AddEndpointFilter<IdempotencyFilter<GuestSignInRequest>>()
             .AllowAnonymous();

        group.MapPost("/register", RegisterAsync)
             .AddEndpointFilter<IdempotencyFilter<RegisterRequest>>()
             .AllowAnonymous();

        group.MapPost("/login", LoginAsync).AllowAnonymous();

        group.MapPost("/link", LinkAsync)
             .AddEndpointFilter<IdempotencyFilter<RegisterRequest>>()
             .RequireAuthorization();

        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();

        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
    }

    // ------------------------------------------------------------- handlers

    private static async Task<IResult> GuestAsync(
        GuestSignInRequest request, AuthService auth, CancellationToken ct)
    {
        if (Validate.Guest(request) is { Count: > 0 } errors)
            return ApiProblems.ValidationFailed(errors).ToResult();

        var outcome = await auth.GuestSignInAsync(request.DeviceId, ct);

        if (!outcome.IsSuccess) return outcome.Problem!.ToResult();

        // 201 only when this call is what created the player; a device signing
        // in again gets 200, which is what tells the client it is not new here.
        return Results.Json(outcome.Value, statusCode: outcome.Created ? 201 : 200);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, AuthService auth, CancellationToken ct)
    {
        if (Validate.Register(request) is { Count: > 0 } errors)
            return ApiProblems.ValidationFailed(errors).ToResult();

        var outcome = await auth.RegisterAsync(request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: 201)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, AuthService auth, CancellationToken ct)
    {
        // 登录不做格式校验 / Deliberately no format validation. Answering 422 for
        // a password that is too short tells an attacker the rules without
        // costing them an attempt, and a legitimate user with an old password
        // that no longer meets current rules must still be able to sign in.
        var outcome = await auth.LoginAsync(request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: 200)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> LinkAsync(
        RegisterRequest request, ClaimsPrincipal user, AuthService auth, CancellationToken ct)
    {
        if (Validate.Register(request) is { Count: > 0 } errors)
            return ApiProblems.ValidationFailed(errors).ToResult();

        if (!user.TryGetAccountId(out Guid accountId))
            return ApiProblems.Unauthorized().ToResult();

        var outcome = await auth.LinkAsync(accountId, request, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: 200)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request, AuthService auth, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ApiProblems.RefreshTokenInvalid().ToResult();

        var outcome = await auth.RefreshAsync(request.RefreshToken, ct);

        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: 200)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest request, AuthService auth, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await auth.LogoutAsync(request.RefreshToken, ct);
        }

        // Always 204, including for a token that never existed. Reporting the
        // difference would turn this into a way to test whether a stolen string
        // is live.
        return Results.NoContent();
    }

    // ----------------------------------------------------------- validation

    private static partial class Validate
    {
        [GeneratedRegex("^[A-Za-z0-9_]+$")]
        private static partial Regex UsernameShape();

        public static Dictionary<string, string[]> Guest(GuestSignInRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (request.DeviceId is not { Length: >= 8 and <= 128 })
                errors["deviceId"] = ["Must be between 8 and 128 characters."];

            return errors;
        }

        public static Dictionary<string, string[]> Register(RegisterRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (request.Username is not { Length: >= 3 and <= 24 } || !UsernameShape().IsMatch(request.Username))
                errors["username"] = ["Must be 3 to 24 characters, letters, digits and underscore only."];

            if (request.Password is not { Length: >= 8 and <= 128 })
                errors["password"] = ["Must be between 8 and 128 characters."];

            return errors;
        }
    }
}
