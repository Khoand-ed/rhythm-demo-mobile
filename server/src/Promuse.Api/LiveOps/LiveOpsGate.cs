using Promuse.Api.Infrastructure;
using Promuse.Contracts.Config;
using Promuse.Persistence;

namespace Promuse.Api.LiveOps;

/// <summary>
/// The two switches that apply to the whole API rather than one endpoint: a minimum client
/// version, and maintenance.
///
/// 版本 / A request carrying an X-Client-Version older than the config's minimum is refused with
/// 426 before it reaches a handler. A request without the header is let through: the header is
/// the game's own claim about itself, so this keeps old builds out rather than keeping anyone
/// determined out - tools, tests and the admin page simply do not send it.
///
/// 维护 / While maintenance is on, every game endpoint answers 503 with the operator's message
/// and, when an end time is set, Retry-After. Administrators pass, so they can check the game
/// before reopening it. A run result refused by maintenance is a transient failure to the client,
/// which keeps it and sends it again later, so nobody loses a clear to the window.
///
/// 哪些路不拦 / Never gated: the config itself (a client has to be able to learn why it is being
/// refused), health checks, the admin API and page. Sign-in is gated by version but not by
/// maintenance, because an administrator has to be able to sign in to turn maintenance off.
/// </summary>
public sealed class LiveOpsGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RemoteConfigStore store, PromuseDbContext db)
    {
        PathString path = http.Request.Path;

        if (AlwaysOpen(path))
        {
            await next(http);
            return;
        }

        CancellationToken ct = http.RequestAborted;
        RemoteConfigStore.Snapshot config = await store.GetAsync(ct);

        string? client = http.Request.Headers[ClientVersion.Header];
        if (ClientVersion.IsOutdated(client, config.Document.MinClientVersion))
        {
            await ApiProblems.ClientOutdated(config.Document.MinClientVersion).ToResult().ExecuteAsync(http);
            return;
        }

        MaintenanceWindow maintenance = config.Document.Maintenance;

        if (maintenance.Enabled && !path.StartsWithSegments("/v1/auth")
            && !await RemoteConfigService.IsAdminAsync(db, http.User, ct))
        {
            if (maintenance.EndsAt is { } end && end > DateTimeOffset.UtcNow)
            {
                http.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalSeconds)).ToString();
            }

            await ApiProblems.Maintenance(maintenance.Message, maintenance.EndsAt).ToResult().ExecuteAsync(http);
            return;
        }

        await next(http);
    }

    private static bool AlwaysOpen(PathString path) =>
        path == "/"
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/v1/config")
        || path.StartsWithSegments("/v1/admin")
        || path.StartsWithSegments("/admin");
}

/// <summary>
/// Refuses one endpoint while its feature flag is off. Added before the idempotency filter, so a
/// refused request never takes a key - pressing again once the feature is back is a fresh request.
/// </summary>
public sealed class FeatureGate(Func<FeatureFlags, bool> isOn, string feature) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var store = context.HttpContext.RequestServices.GetRequiredService<RemoteConfigStore>();
        RemoteConfigStore.Snapshot config = await store.GetAsync(context.HttpContext.RequestAborted);

        return isOn(config.Document.Features)
            ? await next(context)
            : ApiProblems.FeatureDisabled(feature).ToResult();
    }
}

public static class FeatureGateExtensions
{
    public static RouteHandlerBuilder RequireFeature(
        this RouteHandlerBuilder builder, Func<FeatureFlags, bool> isOn, string feature) =>
        builder.AddEndpointFilter(new FeatureGate(isOn, feature));
}

/// <summary>Lets only administrators through; everyone else signed in gets 403.</summary>
public sealed class AdminOnly : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<PromuseDbContext>();

        return await RemoteConfigService.IsAdminAsync(db, context.HttpContext.User, context.HttpContext.RequestAborted)
            ? await next(context)
            : ApiProblems.Forbidden().ToResult();
    }
}
