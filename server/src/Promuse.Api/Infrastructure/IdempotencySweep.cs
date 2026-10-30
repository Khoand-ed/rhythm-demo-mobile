using Microsoft.EntityFrameworkCore;
using Promuse.Persistence;

namespace Promuse.Api.Infrastructure;

/// <summary>
/// Deletes idempotency records past their 24 hours, once an hour.
///
/// 只是打扫 / Housekeeping, not correctness. The filter already treats an expired record as
/// absent and deletes one it meets, so a sweep that comes late - or never, in a process that does
/// not live an hour - replays nothing it should not. What it bounds is the table: every request
/// through the filter leaves a row, and most keys are never presented again.
///
/// 一批一批删 / A batch at a time, oldest first, through the index on expires_at. Each DELETE is
/// short and holds a thousand row locks at most, rather than one statement taking a day's worth -
/// or, the first time it runs, every record since the table was made.
///
/// 不用排队 / No gate, unlike the filter. Deleting a record that has already expired changes no
/// request's answer: expired, it answers nothing whether its row is there or not. A row the filter
/// is deleting at the same moment is deleted once, and two instances sweeping together only find
/// less to do.
///
/// 一小时后才第一次 / The first pass is an hour after start, not at it. Nothing is lost by waiting,
/// and a host that has only just started may be facing a database that is not migrated yet - the
/// test host is migrated after it starts.
/// </summary>
public sealed class IdempotencySweep(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<IdempotencySweep> log) : BackgroundService
{
    /// <summary>How often. A record outlives its 24 hours by an hour at most.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <summary>Rows per DELETE.</summary>
    public const int BatchSize = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(BatchSize, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // 下个小时再试 / Never out of here: an exception leaving a background service stops
                // the whole host, so a database that is away for a moment would take the API down
                // with it. The next pass tries again.
                log.LogError(e, "Deleting expired idempotency records failed; the next pass is in {Every}.", Every);
            }
        }
    }

    /// <summary>
    /// One pass: every record expired at the moment it starts, <paramref name="batchSize"/> rows at a
    /// time, until a batch comes back short. A record expiring meanwhile is the next pass's.
    /// </summary>
    /// <returns>How many records were deleted.</returns>
    public async Task<int> SweepAsync(int batchSize, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        using IServiceScope scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        int total = 0;
        int deleted;

        do
        {
            // 和 IsLive 正好相反 / Exactly what IdempotencyRecord.IsLive says is not live.
            deleted = await db.IdempotencyRecords
                .Where(r => r.ExpiresAt <= now)
                .OrderBy(r => r.ExpiresAt)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
        }
        while (deleted == batchSize);

        if (total > 0) log.LogInformation("Deleted {Count} expired idempotency record(s).", total);

        return total;
    }
}
