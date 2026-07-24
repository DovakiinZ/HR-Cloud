using System.Diagnostics;
using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Audit;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Completion;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>
/// The single orchestration layer between approved requests and business modules. It materializes
/// the request's completion effects, runs them in order inside ONE transaction, records per-effect
/// status/timing/target + an audit entry, and on any failure rolls everything back, persists the
/// failure, and notifies support — so no partial cross-module data ever remains.
/// </summary>
public sealed class CompletionEngine : ICompletionEngine
{
    private readonly ApplicationDbContext _db;
    private readonly ICompletionEffectFactory _factory;
    private readonly IEffectExecutorRegistry _registry;
    private readonly ITimelineEngine _timeline;
    private readonly IAuditEngine _audit;
    private readonly INotificationService _notify;
    private readonly ICurrentUserService _user;

    private static readonly JsonSerializerOptions Json = new();

    public CompletionEngine(
        ApplicationDbContext db,
        ICompletionEffectFactory factory,
        IEffectExecutorRegistry registry,
        ITimelineEngine timeline,
        IAuditEngine audit,
        INotificationService notify,
        ICurrentUserService user)
    {
        _db = db; _factory = factory; _registry = registry;
        _timeline = timeline; _audit = audit; _notify = notify; _user = user;
    }

    public async Task<CompletionResult> ExecuteAsync(Guid requestInstanceId, CancellationToken ct)
    {
        var instance = await _db.RequestInstances
            .Include(r => r.RequestType).ThenInclude(t => t.ImpactMapping)
            .FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new InvalidOperationException($"RequestInstance {requestInstanceId} not found.");

        var ctxBase = new
        {
            instance.RequestNumber,
            TypeCode = instance.RequestType.Code,
            instance.EmployeeId,
            instance.WorkflowInstanceId,
            AffectsTimeline = instance.RequestType.ImpactMapping?.AffectsTimeline ?? true,
        };

        // ── Phase A: materialize intents + persist the run (committed immediately, so the
        //    completion record survives even if the effect transaction later rolls back). This
        //    SaveChanges also flushes any pending approval/workflow changes from the caller.
        var intents = await _factory.BuildAsync(requestInstanceId, ct);

        var run = new CompletionRun
        {
            RequestInstanceId = requestInstanceId,
            WorkflowInstanceId = ctxBase.WorkflowInstanceId,
            Status = CompletionRunStatus.Executing,
            StartedAt = DateTime.UtcNow,
            Attempts = 1,
            FinalApproverUserId = _user.IsAuthenticated ? _user.UserId : null,
        };
        var deferredSequences = new HashSet<int>();
        foreach (var intent in intents.OrderBy(i => i.Sequence))
        {
            var deferred = intent.Mode == EffectExecutionMode.Deferred;
            if (deferred) deferredSequences.Add(intent.Sequence);
            run.Effects.Add(new CompletionEffect
            {
                RequestInstanceId = requestInstanceId,
                EffectType = intent.EffectType,
                Sequence = intent.Sequence,
                Payload = intent.Payload,
                MaxAttempts = deferred ? Math.Max(1, intent.MaxAttempts) : 1,
                ScheduledFor = deferred ? intent.ScheduledFor : null,
                Status = deferred
                    ? (intent.ScheduledFor is { } when && when > DateTime.UtcNow
                        ? CompletionEffectStatus.Scheduled
                        : CompletionEffectStatus.Pending)
                    : CompletionEffectStatus.Pending,
            });
        }
        _db.CompletionRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // Deferred effects get their Id as idempotency key now that they are persisted.
        if (deferredSequences.Count > 0)
        {
            foreach (var e in run.Effects.Where(e => deferredSequences.Contains(e.Sequence)))
                e.IdempotencyKey = e.Id.ToString();
            await _db.SaveChangesAsync(ct);
        }

        // No effects to run → completed trivially.
        if (run.Effects.Count == 0)
        {
            run.Status = CompletionRunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
            run.DurationMs = 0;
            await _db.SaveChangesAsync(ct);
            return CompletionResult.Ok(run.Id, 0);
        }

        // ── Phase B: execute only inline (non-deferred) effects atomically.
        var overall = Stopwatch.StartNew();
        var ordered = run.Effects
            .Where(e => !deferredSequences.Contains(e.Sequence))
            .OrderBy(e => e.Sequence).ToList();
        var deferredEffects = run.Effects.Where(e => deferredSequences.Contains(e.Sequence)).ToList();
        CompletionEffect? failed = null;
        string? error = null;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var effect in ordered)
            {
                effect.Status = CompletionEffectStatus.Executing;
                effect.Attempts++;

                var executor = _registry.Resolve(effect.EffectType);
                var context = new EffectContext
                {
                    RequestInstanceId = requestInstanceId,
                    RequestNumber = ctxBase.RequestNumber,
                    RequestTypeCode = ctxBase.TypeCode,
                    EmployeeId = ctxBase.EmployeeId,
                    ActorUserId = _user.IsAuthenticated ? _user.UserId : null,
                    Payload = JsonDocument.Parse(effect.Payload).RootElement,
                };

                var sw = Stopwatch.StartNew();
                EffectExecutionResult result;
                try
                {
                    result = await executor.ExecuteAsync(context, ct);
                }
                catch (Exception ex)
                {
                    failed = effect;
                    error = ex.Message;
                    throw;
                }
                sw.Stop();

                // A skip is not a failure: the run continues and nothing rolls back. It is recorded
                // distinctly so completion history shows "this did not happen, and why" rather than
                // a success that quietly did nothing.
                effect.Status = result.IsSkipped ? CompletionEffectStatus.Skipped : CompletionEffectStatus.Completed;
                if (result.IsSkipped) effect.FailureReason = result.SkipReason;
                effect.ExecutedAt = DateTime.UtcNow;
                effect.DurationMs = (int)sw.ElapsedMilliseconds;
                effect.ExecutorName = executor.GetType().Name;
                effect.ExecutorVersion = executor.Version;
                effect.TargetEntityType = result.TargetEntityType;
                effect.TargetRecordId = result.TargetRecordId;
                effect.ResultSummary = JsonSerializer.Serialize(new { summary = result.Summary, after = result.AfterState }, Json);

                await _audit.LogChange(
                    result.TargetEntityType ?? effect.EffectType,
                    result.TargetRecordId ?? requestInstanceId,
                    $"Completion:{effect.EffectType}",
                    result.BeforeState,
                    result.AfterState,
                    ct);

                await _db.SaveChangesAsync(ct);
            }

            overall.Stop();
            run.Status = deferredEffects.Count > 0
                ? CompletionRunStatus.AwaitingDeferred
                : CompletionRunStatus.Completed;
            run.CompletedAt = deferredEffects.Count > 0 ? null : DateTime.UtcNow;
            run.DurationMs = (int)overall.ElapsedMilliseconds;

            if (ctxBase.AffectsTimeline)
                await _timeline.PublishEvent("Completion", "RequestInstance", requestInstanceId, "RequestCompleted",
                    $"Completion applied {ordered.Count} effect(s) for {ctxBase.RequestNumber}",
                    $"تم تنفيذ {ordered.Count} إجراء للطلب {ctxBase.RequestNumber}",
                    new { effects = ordered.Select(e => e.EffectType).ToArray() }, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return CompletionResult.Ok(run.Id, ordered.Count);
        }
        catch (Exception ex)
        {
            // Roll back every module mutation made this run, then discard the tracked (now-reverted)
            // changes so persisting the failure record does not re-apply them.
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            error ??= ex.Message;

            await PersistFailureAsync(run.Id, failed?.Sequence, error, ctxBase.RequestNumber, ctxBase.EmployeeId, ct);
            return CompletionResult.Fail(run.Id, error);
        }
    }

    /// <summary>Records the failure on the (already-committed) run + failing effect and notifies an admin.</summary>
    private async Task PersistFailureAsync(Guid runId, int? failedSequence, string error, string requestNumber, Guid employeeId, CancellationToken ct)
    {
        var run = await _db.CompletionRuns.Include(r => r.Effects).FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is not null)
        {
            run.Status = CompletionRunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            run.FailureReason = Truncate(error, 2000);

            foreach (var e in run.Effects)
            {
                if (failedSequence is { } fs && e.Sequence == fs)
                {
                    e.Status = CompletionEffectStatus.Failed;
                    e.ExecutedAt = DateTime.UtcNow;
                    e.FailureReason = Truncate(error, 2000);
                }
                else if (e.Status != CompletionEffectStatus.Completed)
                {
                    e.Status = CompletionEffectStatus.Cancelled;
                }
            }
        }

        var adminUserId = await AdminUserIdAsync(ct);
        if (adminUserId is { } uid)
            await _notify.NotifyAsync(uid,
                "فشل تنفيذ طلب معتمَد", "Approved request failed to complete",
                $"تعذّر تنفيذ إجراءات الطلب {requestNumber}: {error}",
                $"Completion failed for request {requestNumber}: {error}",
                "CompletionFailure", runId, "/requests", email: true, ct: ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Guid?> AdminUserIdAsync(CancellationToken ct)
    {
        var tid = _user.TenantId;
        var admin = await (from u in _db.Users.Where(u => u.TenantId == tid && u.IsActive)
                           join ur in _db.UserRoles on u.Id equals ur.UserId
                           join r in _db.Roles on ur.RoleId equals r.Id
                           where r.IsSystemRole || EF.Functions.ILike(r.Name, "%admin%")
                           select (Guid?)u.Id).FirstOrDefaultAsync(ct);
        return admin ?? await _db.Users.Where(u => u.TenantId == tid && u.IsActive).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
