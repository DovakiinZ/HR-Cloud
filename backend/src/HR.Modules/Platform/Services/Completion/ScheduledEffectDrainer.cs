using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Completion;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>Drains due deferred completion effects: claim a batch across tenants, execute each in its own
/// transaction through the shared executor registry, and apply the pure retry decision. One row failing
/// never aborts the batch. Mirrors EmailQueueDrainer.</summary>
public sealed class ScheduledEffectDrainer : IScheduledEffectDrainer
{
    private const int BatchSize = 25;
    private static readonly TimeSpan LeaseWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);

    private readonly ApplicationDbContext _db;
    private readonly IEffectExecutorRegistry _registry;
    private readonly IBackgroundExecutionContext _background;
    private readonly ILogger<ScheduledEffectDrainer> _logger;
    private readonly Func<DateTime> _clock;
    private readonly string _workerId;

    public ScheduledEffectDrainer(
        ApplicationDbContext db, IEffectExecutorRegistry registry,
        IBackgroundExecutionContext background, ILogger<ScheduledEffectDrainer> logger,
        Func<DateTime>? clock = null)
    {
        _db = db; _registry = registry; _background = background; _logger = logger;
        _clock = clock ?? (() => DateTime.UtcNow);
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
    }

    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var now = _clock();

        // Claim a batch across all tenants. Due = ready-to-run status, date reached, retry gate passed,
        // attempts left, and either unleased or the lease has expired (crash recovery).
        var batch = await _db.CompletionEffects.IgnoreQueryFilters()
            .Where(e =>
                (e.Status == CompletionEffectStatus.Pending
                 || e.Status == CompletionEffectStatus.Scheduled
                 || e.Status == CompletionEffectStatus.Retrying
                 || (e.Status == CompletionEffectStatus.Executing && e.LeasedUntil != null && e.LeasedUntil < now))
                && (e.ScheduledFor == null || e.ScheduledFor <= now)
                && (e.NextAttemptAt == null || e.NextAttemptAt <= now)
                && e.Attempts < e.MaxAttempts
                && (e.LeasedUntil == null || e.LeasedUntil < now))
            .OrderBy(e => e.ScheduledFor ?? e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        // Lease the claimed rows first so an overlapping tick cannot re-grab them.
        foreach (var e in batch)
        {
            e.Status = CompletionEffectStatus.Executing;
            e.LeasedBy = _workerId;
            e.LeasedUntil = now.Add(LeaseWindow);
        }
        await _db.SaveChangesAsync(ct);

        var processed = 0;
        foreach (var effect in batch)
        {
            if (ct.IsCancellationRequested) break;
            processed += await RunOneAsync(effect, ct) ? 1 : 0;
        }
        return processed;
    }

    private async Task<bool> RunOneAsync(CompletionEffect effect, CancellationToken ct)
    {
        var tenantId = effect.TenantId;
        var startedAt = _clock();
        effect.Attempts++;

        using (_background.Begin(tenantId, null))
        {
            // Load the request context this effect belongs to (for EffectContext).
            var instance = await _db.RequestInstances.IgnoreQueryFilters()
                .Include(r => r.RequestType)
                .FirstOrDefaultAsync(r => r.Id == effect.RequestInstanceId, ct);

            EffectExecutionResult? result = null;
            string? error = null;
            var permanent = false;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var executor = _registry.Resolve(effect.EffectType);
                var context = new EffectContext
                {
                    RequestInstanceId = effect.RequestInstanceId,
                    RequestNumber = instance?.RequestNumber ?? "",
                    RequestTypeCode = instance?.RequestType?.Code ?? "",
                    EmployeeId = instance?.EmployeeId ?? Guid.Empty,
                    ActorUserId = null,
                    IdempotencyKey = effect.IdempotencyKey,
                    Payload = JsonDocument.Parse(effect.Payload).RootElement,
                };

                result = await executor.ExecuteAsync(context, ct);
                effect.ExecutorName = executor.GetType().Name;
                effect.ExecutorVersion = executor.Version;
                ScheduledEffectDecision.ApplySuccess(effect, result, _clock());
                RecordAttempt(effect, startedAt);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                error = ex.Message;
                permanent = ex is NonRetryableEffectException;
                _logger.LogWarning(ex,
                    "Scheduled-effect {EffectId} ({EffectType}) attempt {Attempt} failed (permanent={Permanent}).",
                    effect.Id, effect.EffectType, effect.Attempts, permanent);
            }

            // Failure path: re-load the tracked row (cleared above) and persist the decision.
            var row = await _db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id, ct);
            row.Attempts = effect.Attempts;
            ScheduledEffectDecision.ApplyFailure(row, error!, permanent, _clock(), BaseBackoff, MaxBackoff);
            RecordAttempt(row, startedAt);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }

    private void RecordAttempt(CompletionEffect effect, DateTime startedAt)
    {
        _db.EffectAttempts.Add(new EffectAttempt
        {
            TenantId = effect.TenantId,
            CompletionEffectId = effect.Id,
            AttemptNumber = effect.Attempts,
            StartedAt = startedAt,
            Status = effect.Status,
            DurationMs = (int)(_clock() - startedAt).TotalMilliseconds,
            FailureReason = effect.Status is CompletionEffectStatus.Retrying or CompletionEffectStatus.ManualReview
                ? effect.FailureReason : null,
        });
    }
}
