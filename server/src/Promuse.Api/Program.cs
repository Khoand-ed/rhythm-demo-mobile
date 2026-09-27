using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Promuse.Api.Auth;
using Promuse.Api.Infrastructure;
using Promuse.Api.Economy;
using Promuse.Api.Players;
using Promuse.Api.Runs;
using Promuse.Persistence;

// 必须显式传 / WriteAsJsonAsync sets Content-Type itself and overwrites
// anything already on the response, so the media type has to be passed to it
// rather than assigned beforehand - and it is the FOURTH parameter, after
// JsonSerializerOptions. Without this every error left as
// application/json while the contract promises problem+json - which an
// integration test caught, and a reader of this file would not have.
const string ProblemJson = "application/problem+json";

var builder = WebApplication.CreateBuilder(args);

// The connection string is configuration, never a literal. Locally it comes
// from appsettings.Development.json; anywhere else from the environment, which
// is why there is no fallback here - a missing one should stop the process at
// startup rather than surface later as a confusing connection error.
var connectionString = builder.Configuration.GetConnectionString("Promuse")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Promuse is not configured. For local development, copy " +
        "server/env.example to server/.env, start the database with " +
        "`docker compose -f server/docker-compose.yml up -d`, and check " +
        "appsettings.Development.json.");

builder.Services.AddDbContext<PromuseDbContext>(options =>
    options.UseNpgsql(connectionString)
           // Postgres is case-folding and snake_case by convention, so the
           // schema reads the same from psql as it does from here.
           .UseSnakeCaseNamingConvention());

// 启动就验证 / Validated at startup rather than on first use. A missing or
// too-short signing key is a security hole, and one that only appears the first
// time someone signs in is a hole that ships.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<StaminaOptions>()
    .Bind(builder.Configuration.GetSection(StaminaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<EconomyService>();
builder.Services.AddScoped<RunService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException($"Configuration section '{JwtOptions.SectionName}' is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keeps `sub` as `sub`. Left on, the handler rewrites it to a long
        // ClaimTypes URI and every lookup has to know that happened.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // The default is five minutes, which would let an expired token keep
            // working for five more. Access tokens here are fifteen minutes, so
            // that default is a third of their whole lifetime.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.Events = new JwtBearerEvents
        {
            // Without this the framework answers 401 with an empty body, and a
            // client that parses one error shape everywhere would find nothing
            // to parse.
            OnChallenge = async context =>
            {
                context.HandleResponse();

                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(
                    ApiProblems.Unauthorized(), options: null, contentType: ProblemJson);
            },
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    // 可配置是故意的 / Configurable because the right number is an operational
    // decision, not a compile-time one - and because the integration tests raise
    // it to run a hundred requests without tripping a limiter they are not
    // testing.
    int permitPerMinute = builder.Configuration.GetValue("RateLimit:AuthPermitPerMinute", 20);

    options.AddPolicy(RateLimitPolicies.Auth, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Per caller, not global: one abusive address must not be able to
            // lock every player out of signing in.
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiProblems.RateLimited(), options: null, contentType: ProblemJson, cancellationToken: ct);
    };
});

builder.Services.AddHealthChecks()
    // Tagged so the two endpoints below can select different sets. A liveness
    // probe that also checks the database is a restart loop waiting to happen:
    // the database going away restarts every instance, which fixes nothing and
    // removes the capacity that would have served cached reads.
    .AddDbContextCheck<PromuseDbContext>("database", tags: ["ready"]);

var app = builder.Build();

// 所有失败一个形状 / Every failure leaves as the same problem+json shape,
// including the ones nobody planned for. The trace id is the only part of a 500
// that is safe to show and the only part worth asking a player to quote.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    string traceId = context.TraceIdentifier;

    app.Logger.LogError(feature?.Error, "Unhandled exception. TraceId {TraceId}", traceId);

    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(
        ApiProblems.Internal(traceId), options: null, contentType: ProblemJson);
}));

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is running and can answer. Nothing else - by design.
app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false,
});

// Readiness: this instance can actually serve, database included. This is the
// one an orchestrator should gate traffic on.
app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapGet("/", () => Results.Ok(new
{
    service = "promuse-api",
    // Deliberately not the assembly version or the commit: an unauthenticated
    // root endpoint should not tell a stranger which build is deployed and
    // therefore which advisories apply to it.
    status = "ok",
}));

app.MapAuthEndpoints();
app.MapPlayerEndpoints();
app.MapEconomyEndpoints();
app.MapRunEndpoints();

app.Run();

/// <summary>
/// Named so WebApplicationFactory can find the entry point. A top-level program
/// compiles to an internal Program class, which the test host cannot reach.
/// </summary>
public partial class Program;
