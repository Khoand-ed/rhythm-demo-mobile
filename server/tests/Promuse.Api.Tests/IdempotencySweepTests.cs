using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Promuse.Api.Infrastructure;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Tests;

/// <summary>
/// The hourly sweep of idempotency records past their 24 hours.
///
/// 自己的钟 / Each test makes a sweep of its own, on the suite's database, with a clock it moves by
/// hand. The host's own sweep runs on the real clock, and its first pass is an hour after the host
/// starts - so it never runs while the suite does.
///
/// 只看自己的行 / Every other test class leaves records in the same table, and a pass deletes every
/// expired record there is. So these plant rows under an endpoint of their own and look only at
/// those - never at how many rows a pass deleted in all.
/// </summary>
[Collection(ApiCollection.Name)]
public class IdempotencySweepTests(PromuseApiFactory factory)
{
    private static string Unique => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Now, to the microsecond - what Postgres keeps. A record set to expire at exactly this moment
    /// is stored as exactly this moment.
    /// </summary>
    private static DateTimeOffset Start
    {
        get
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        }
    }

    // --------------------------------------------------------------- helpers

    /// <summary>
    /// A clock moved by hand that also says when the sweep has set its timer: only an advance made
    /// after that can fire it.
    /// </summary>
    private sealed class Clock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private readonly TaskCompletionSource _timerSet = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TimerSet => _timerSet.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = base.CreateTimer(callback, state, dueTime, period);
            _timerSet.TrySetResult();
            return timer;
        }
    }

    /// <summary>The scopes a request would get, except that the first <paramref name="failures"/> fail.</summary>
    private sealed class Failing(IServiceScopeFactory scopes, int failures) : IServiceScopeFactory
    {
        private int _left = failures;

        public IServiceScope CreateScope() =>
            Interlocked.Decrement(ref _left) >= 0
                ? throw new InvalidOperationException("The database is away.")
                : scopes.CreateScope();
    }

    private IdempotencySweep Sweep(TimeProvider clock, ILogger<IdempotencySweep>? log = null,
                                   IServiceScopeFactory? scopes = null) =>
        new(scopes ?? factory.Services.GetRequiredService<IServiceScopeFactory>(), clock,
            log ?? NullLogger<IdempotencySweep>.Instance);

    /// <summary>One record under <paramref name="endpoint"/> for each expiry given.</summary>
    private async Task PlantAsync(string endpoint, params DateTimeOffset[] expiries)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        foreach (DateTimeOffset expiresAt in expiries)
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                Key = Unique,
                Endpoint = endpoint,
                RequestHash = "planted",
                StatusCode = 200,
                ResponseBody = "{}",
                CreatedAt = expiresAt.AddHours(-24),
                ExpiresAt = expiresAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>When each record still under <paramref name="endpoint"/> expires, soonest first.</summary>
    private async Task<List<DateTimeOffset>> LeftAsync(string endpoint)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        return await db.IdempotencyRecords
            .Where(r => r.Endpoint == endpoint)
            .OrderBy(r => r.ExpiresAt)
            .Select(r => r.ExpiresAt)
            .ToListAsync();
    }

    /// <summary>
    /// 等后台那一趟 / Waits for something the sweep does on its own thread once its timer fires.
    /// </summary>
    private static async Task UntilAsync(Func<Task<bool>> done, string what)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(15);

        while (!await done())
        {
            if (DateTime.UtcNow > giveUp) Assert.Fail($"Waited 15 seconds, and still not: {what}");
            await Task.Delay(50);
        }
    }

    // ----------------------------------------------------------------- tests

    [Fact]
    public void The_api_runs_the_sweep()
    {
        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<IdempotencySweep>());
    }

    /// <summary>
    /// 一批一批，删干净 / More expired records than a batch holds, so the pass has to go round more
    /// than once - a pass that stopped after one batch would leave some. One expires at the very
    /// moment the pass starts, which is expired: a record is live only while its expiry is still to
    /// come. The live ones, the nearest a millisecond away, are left alone.
    /// </summary>
    [Fact]
    public async Task A_pass_deletes_every_expired_record_a_batch_at_a_time_and_none_still_live()
    {
        DateTimeOffset now = Start;
        string endpoint = "sweep " + Unique;

        DateTimeOffset[] live = [now.AddMilliseconds(1), now.AddHours(1), now.AddHours(24)];
        await PlantAsync(endpoint, live);
        await PlantAsync(endpoint,
            now, now.AddMilliseconds(-1), now.AddMinutes(-1), now.AddHours(-1),
            now.AddDays(-1), now.AddDays(-7), now.AddDays(-30));

        int deleted = await Sweep(new FakeTimeProvider(now)).SweepAsync(batchSize: 3, CancellationToken.None);

        Assert.Equal(live, await LeftAsync(endpoint));
        Assert.True(deleted >= 7, $"{deleted} deleted, fewer than the 7 expired ones planted");
    }

    /// <summary>
    /// 一小时一次 / Started, the sweep waits: its first pass is an hour later, and each one after
    /// that an hour after the last, deleting what has expired by then. A record that was still live
    /// at the first pass goes at the second.
    /// </summary>
    [Fact]
    public async Task The_sweep_runs_an_hour_after_it_starts_and_every_hour_after()
    {
        DateTimeOffset now = Start;
        string endpoint = "sweep " + Unique;
        DateTimeOffset betweenPasses = now.AddMinutes(90);

        await PlantAsync(endpoint, now.AddMinutes(-1), betweenPasses, now.AddHours(24));

        var clock = new Clock(now);
        using IdempotencySweep sweep = Sweep(clock);

        await sweep.StartAsync(CancellationToken.None);
        try
        {
            await clock.TimerSet.WaitAsync(TimeSpan.FromSeconds(15));

            // Started, and nothing yet: an hour less a moment is not an hour.
            clock.Advance(IdempotencySweep.Every - TimeSpan.FromTicks(1));
            await Task.Delay(200);
            Assert.Equal(3, (await LeftAsync(endpoint)).Count);

            clock.Advance(TimeSpan.FromTicks(1));
            await UntilAsync(async () => (await LeftAsync(endpoint)).Count == 2, "the first pass");
            Assert.Equal([betweenPasses, now.AddHours(24)], await LeftAsync(endpoint));

            clock.Advance(IdempotencySweep.Every);
            await UntilAsync(async () => (await LeftAsync(endpoint)).Count == 1, "the second pass");
            Assert.Equal([now.AddHours(24)], await LeftAsync(endpoint));
        }
        finally
        {
            await sweep.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 失败不能停 / A pass that throws is logged, and the next hour's pass runs as usual. An exception
    /// leaving a background service stops the whole host, so a database that is away for a moment at
    /// the top of the hour would otherwise take the API down with it.
    /// </summary>
    [Fact]
    public async Task A_pass_that_fails_is_logged_and_the_next_one_still_runs()
    {
        DateTimeOffset now = Start;
        string endpoint = "sweep " + Unique;

        await PlantAsync(endpoint, now.AddMinutes(-1));

        var clock = new Clock(now);
        var log = new FakeLogger<IdempotencySweep>();
        using IdempotencySweep sweep = Sweep(clock, log,
            new Failing(factory.Services.GetRequiredService<IServiceScopeFactory>(), failures: 1));

        await sweep.StartAsync(CancellationToken.None);
        try
        {
            await clock.TimerSet.WaitAsync(TimeSpan.FromSeconds(15));

            clock.Advance(IdempotencySweep.Every);
            await UntilAsync(() => Task.FromResult(log.Collector.Count > 0), "the failed pass logged");

            FakeLogRecord failed = log.LatestRecord;
            Assert.Equal(LogLevel.Error, failed.Level);
            Assert.IsType<InvalidOperationException>(failed.Exception);
            Assert.Single(await LeftAsync(endpoint));
            Assert.False(sweep.ExecuteTask!.IsCompleted, "the sweep stopped after a failed pass");

            clock.Advance(IdempotencySweep.Every);
            await UntilAsync(async () => (await LeftAsync(endpoint)).Count == 0, "the pass after it");
        }
        finally
        {
            await sweep.StopAsync(CancellationToken.None);
        }
    }
}
