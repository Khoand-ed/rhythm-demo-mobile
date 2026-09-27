using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Promuse.Persistence;

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
           // schema reads the same from psql as it does from here. Set once at
           // registration rather than spelled out per column.
           .UseSnakeCaseNamingConvention());

builder.Services.AddHealthChecks()
    // Tagged so the two endpoints below can select different sets. A liveness
    // probe that also checks the database is a restart loop waiting to happen:
    // the database going away restarts every instance, which fixes nothing and
    // removes the capacity that would have served cached reads.
    .AddDbContextCheck<PromuseDbContext>("database", tags: ["ready"]);

var app = builder.Build();

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

app.Run();
