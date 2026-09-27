using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Persistence;
using Testcontainers.PostgreSql;

namespace Promuse.Api.Tests;

/// <summary>
/// The real API against a real Postgres.
///
/// 为什么不是内存库 / Not an in-memory provider, and the reason is the whole
/// point of these tests. Idempotency here is a unique index refusing a second
/// insert, and refresh rotation is a conditional UPDATE reporting how many rows
/// it touched. An in-memory provider implements neither faithfully, so it would
/// pass on exactly the code paths worth doubting.
///
/// 每个类一个容器 / One container per test class (see the collection below),
/// started once and migrated once. Postgres takes a second or two to come up,
/// which is affordable once and not affordable per test.
/// </summary>
public sealed class PromuseApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Image on the constructor rather than through WithImage: the parameterless
    // overload is obsolete in Testcontainers 4.15 and warns.
    //
    // Same minor line as docker-compose.yml and the CI service. Testing against
    // a different Postgres than production runs is how a migration passes here
    // and fails there.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine")
        .WithDatabase("promuse_test")
        .WithUsername("promuse")
        .WithPassword("test_only")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Promuse", _postgres.GetConnectionString());

        // 把限流调高 / Raised so the limiter does not reject requests from tests
        // that are not testing the limiter. RateLimitTests lowers it back for
        // itself by driving the endpoint past this number deliberately.
        builder.UseSetting("RateLimit:AuthPermitPerMinute", "10000");

        builder.UseSetting("Jwt:Issuer", "promuse-test");
        builder.UseSetting("Jwt:Audience", "promuse-test-client");
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-at-least-32-bytes-long");
    }

    // 显式实现 / Explicit, because xunit v2's IAsyncLifetime.DisposeAsync returns
    // Task while WebApplicationFactory already has a public ValueTask
    // DisposeAsync. Two methods of the same name cannot differ only by return
    // type, so the interface's version is implemented under the interface and
    // the base class keeps its own.

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrate rather than EnsureCreated: this also proves the migrations
        // themselves are applicable, which EnsureCreated would skip by building
        // the schema straight from the model.
        using IServiceScope scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>
/// Shares one container across every test class in the collection, so the whole
/// suite pays for Postgres starting once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PromuseApiFactory>
{
    public const string Name = "promuse-api";
}
