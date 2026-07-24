using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Application.Engines.Audit;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Completion;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Timeline;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Completion;
using HR.Modules.Platform.Services.Completion.Executors;
using HR.Modules.Platform.Services.Notifications;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

// ── Pilot fakes ───────────────────────────────────────────────────────────────

file sealed class PilotUser : ICurrentUserService
{
    public Guid UserId => Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    public Guid TenantId => Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    public string? Email => "pilot@hr.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

file sealed class PilotTimeline : ITimelineEngine
{
    public Task PublishEvent(string category, string entityType, Guid entityId, string action,
        string? descriptionEn = null, string? descriptionAr = null, object? metadata = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<PaginatedList<TimelineEvent>> GetTimeline(string entityType, Guid entityId,
        int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
        => Task.FromResult(new PaginatedList<TimelineEvent> { PageNumber = 1, PageSize = 20 });
}

file sealed class PilotAudit : IAuditEngine
{
    public Task LogChange(string entityType, Guid entityId, string action, object? oldValues = null,
        object? newValues = null, CancellationToken ct = default)
        => Task.CompletedTask;
}

file sealed class PilotNotifications : INotificationService
{
    public Task NotifyAsync(Guid userId, string titleAr, string titleEn, string bodyAr, string bodyEn,
        string category, Guid? entityId, string link, DateTime? dueAt = null, bool email = true,
        CancellationToken ct = default)
        => Task.CompletedTask;
}

file sealed class PilotLeaveService : ILeaveService
{
    public LeaveRules GetRules(string? metadataJson) => new();
    public decimal ComputeDays(DateTime start, DateTime end, LeaveRules rules) => 0;
    public Task<List<LeaveTypeInfo>> GetLeaveTypesAsync(Guid employeeId, CancellationToken ct) => Task.FromResult(new List<LeaveTypeInfo>());
    public Task<LeavePreview> PreviewAsync(Guid employeeId, Guid leaveTypeId, DateTime? start, DateTime? end, bool hasAttachment, CancellationToken ct)
        => Task.FromResult(new LeavePreview());
}

/// <summary>
/// No-op background context: the drainer calls Begin(); in tests the tenant is already
/// established through the seeded data.
/// </summary>
file sealed class PilotBackground : IBackgroundExecutionContext
{
    public bool IsActive => false;
    public Guid TenantId => Guid.Empty;
    public Guid? UserId => null;
    public string? Email => null;
    public Guid? CorrelationId => null;

    public IDisposable Begin(Guid tenantId, Guid? userId = null, string? email = null, Guid? correlationId = null)
        => new NoOpDisposable();

    private sealed class NoOpDisposable : IDisposable { public void Dispose() { } }
}

/// <summary>
/// A registry that wires exactly one real executor: the EmployeeUpdateFieldExecutor.
/// The db instance is injected at construction time so the executor shares the same
/// in-memory store as the engine and drainer.
/// </summary>
file sealed class PilotRegistry : IEffectExecutorRegistry
{
    private readonly IEffectExecutor _executor;

    public PilotRegistry(ApplicationDbContext db)
        => _executor = new EmployeeUpdateFieldExecutor(db);

    public IEffectExecutor Resolve(string effectType)
        => effectType == _executor.EffectType
            ? _executor
            : throw new InvalidOperationException($"PilotRegistry: no executor for '{effectType}'");

    public bool TryResolve(string effectType, out IEffectExecutor executor)
    {
        executor = _executor;
        return effectType == _executor.EffectType;
    }
}

// ── Harness ───────────────────────────────────────────────────────────────────

/// <summary>
/// Wires the REAL CompletionEngine + REAL CompletionEffectFactory + REAL ScheduledEffectDrainer
/// + REAL EmployeeUpdateFieldExecutor over one in-memory ApplicationDbContext.
///
/// Seeds:
///   - An Employee with a known phone number (whitelisted field).
///   - A FormDefinition + RequestType with ONE Deferred RequestEffectDefinition
///     for "Employee.UpdateField" (MaxAttempts=5).
///   - A FormSubmission with values: fieldKey=phone, newValue=0555000999, effectiveOn=(yesterday).
///   - A RequestInstance (Approved) pointing at that submission and employee.
/// </summary>
file sealed class DeferredPilotHarness : IAsyncDisposable
{
    public CompletionEngine Engine { get; }
    public ScheduledEffectDrainer Drainer { get; }
    public Guid RequestInstanceId { get; }
    public Guid EmployeeId { get; }

    private readonly ApplicationDbContext _db;

    private DeferredPilotHarness(
        ApplicationDbContext db,
        CompletionEngine engine,
        ScheduledEffectDrainer drainer,
        Guid requestInstanceId,
        Guid employeeId)
    {
        _db = db;
        Engine = engine;
        Drainer = drainer;
        RequestInstanceId = requestInstanceId;
        EmployeeId = employeeId;
    }

    /// <param name="field">A whitelisted employee field (e.g. "phone").</param>
    /// <param name="newValue">The value to set that field to.</param>
    /// <param name="effectiveOn">The deferred execution date (pass past date to be immediately due).</param>
    public static async Task<DeferredPilotHarness> CreateAsync(
        string field, string newValue, DateTime effectiveOn)
    {
        var user = new PilotUser();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"DeferredPilot_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            user);

        var tenantId = user.TenantId;

        // ── Seed employee ────────────────────────────────────────────────────
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeNumber = "E-PILOT-01",
            FirstName = "Pilot",
            LastName = "Employee",
            Email = "pilot.emp@hr.local",
            Phone = "0500000000",   // original; will be overwritten by the deferred effect
        };
        db.Set<Employee>().Add(employee);

        // ── Seed form definition ─────────────────────────────────────────────
        var formDef = new FormDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "PILOT_FORM",
            NameEn = "Pilot Form",
            NameAr = "نموذج تجريبي",
            Module = "Platform",
        };
        db.Set<FormDefinition>().Add(formDef);

        // ── Seed request type ────────────────────────────────────────────────
        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "PILOT_DATA_UPDATE",
            NameEn = "Pilot Data Update",
            NameAr = "تحديث بيانات تجريبي",
            FormDefinitionId = formDef.Id,
            IsActive = true,
        };
        db.Set<RequestType>().Add(requestType);

        // ── Seed effect definition ───────────────────────────────────────────
        // ConfigJson maps:
        //   "fieldKey"      ← FormField "fieldCodeField"  (the name of the employee field to change)
        //   "newValue"      ← FormField "newValueField"   (the new value)
        //   "__effectiveOn" ← FormField "effectiveDateField" (drives ScheduledFor on the intent)
        //
        // The source enum must be serialized by NAME (JsonStringEnumConverter is on EffectValueMapping).
        var configJson = """
            {
              "fieldKey":       {"source":"FormField","key":"fieldCodeField"},
              "newValue":       {"source":"FormField","key":"newValueField"},
              "__effectiveOn":  {"source":"FormField","key":"effectiveDateField"}
            }
            """;

        var effectDef = new RequestEffectDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            EffectType = "Employee.UpdateField",
            Trigger = EffectTrigger.FinalApproval,
            ExecutionMode = EffectExecutionMode.Deferred,
            MaxAttempts = 5,
            IsEnabled = true,
            Sequence = 1,
            ConfigurationJson = configJson,
        };
        db.Set<RequestEffectDefinition>().Add(effectDef);

        // ── Seed form submission ─────────────────────────────────────────────
        var submission = new FormSubmission
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FormDefinitionId = formDef.Id,
            SubmittedById = user.UserId,
            SubmittedAt = DateTime.UtcNow,
            Status = FormSubmissionStatus.Submitted,
        };
        db.Set<FormSubmission>().Add(submission);

        // Three form values: the field name, the new value, and the effective-on date
        db.FormSubmissionValues.Add(new FormSubmissionValue
        {
            Id = Guid.NewGuid(),
            FormSubmissionId = submission.Id,
            FormFieldId = Guid.NewGuid(),
            FieldCode = "fieldCodeField",
            Value = field,
        });
        db.FormSubmissionValues.Add(new FormSubmissionValue
        {
            Id = Guid.NewGuid(),
            FormSubmissionId = submission.Id,
            FormFieldId = Guid.NewGuid(),
            FieldCode = "newValueField",
            Value = newValue,
        });
        db.FormSubmissionValues.Add(new FormSubmissionValue
        {
            Id = Guid.NewGuid(),
            FormSubmissionId = submission.Id,
            FormFieldId = Guid.NewGuid(),
            FieldCode = "effectiveDateField",
            // ISO-8601 UTC; one day in the past so the drainer considers it due immediately
            Value = effectiveOn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });

        // ── Seed request instance ────────────────────────────────────────────
        var instance = new RequestInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            RequestNumber = "REQ-PILOT-001",
            EmployeeId = employee.Id,
            FormSubmissionId = submission.Id,
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
        };
        db.Set<RequestInstance>().Add(instance);

        await db.SaveChangesAsync();

        // ── Wire real components ─────────────────────────────────────────────
        var registry = new PilotRegistry(db);

        var factory = new CompletionEffectFactory(db, new PilotLeaveService(), user);

        var engine = new CompletionEngine(
            db, factory, registry,
            new PilotTimeline(), new PilotAudit(), new PilotNotifications(), user);

        var drainer = new ScheduledEffectDrainer(
            db, registry, new PilotBackground(),
            NullLogger<ScheduledEffectDrainer>.Instance);

        return new DeferredPilotHarness(db, engine, drainer, instance.Id, employee.Id);
    }

    // ── Query helpers used by the test assertions ────────────────────────────

    public async Task<CompletionRunStatus> RunStatus()
    {
        var run = await _db.CompletionRuns.IgnoreQueryFilters()
            .FirstAsync(r => r.RequestInstanceId == RequestInstanceId);
        return run.Status;
    }

    public async Task<CompletionEffectStatus> EffectStatus()
    {
        var run = await _db.CompletionRuns.IgnoreQueryFilters()
            .Include(r => r.Effects)
            .FirstAsync(r => r.RequestInstanceId == RequestInstanceId);
        return run.Effects.Single().Status;
    }

    public async Task<string?> EmployeePhone()
    {
        var emp = await _db.Set<Employee>().IgnoreQueryFilters().FirstAsync(e => e.Id == EmployeeId);
        return emp.Phone;
    }

    public async Task<int> AttemptCount()
    {
        var run = await _db.CompletionRuns.IgnoreQueryFilters()
            .Include(r => r.Effects)
            .FirstAsync(r => r.RequestInstanceId == RequestInstanceId);
        var effectId = run.Effects.Single().Id;
        return await _db.EffectAttempts.IgnoreQueryFilters()
            .CountAsync(a => a.CompletionEffectId == effectId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}

// ── Test ──────────────────────────────────────────────────────────────────────

public class DeferredEffectPilotTests
{
    /// <summary>
    /// End-to-end proof that the deferred-effect machinery works:
    ///
    ///   1. Approve  → CompletionEngine persists a Deferred effect (AwaitingDeferred);
    ///                 the employee field is NOT yet changed.
    ///   2. Drain    → ScheduledEffectDrainer executes the real EmployeeUpdateFieldExecutor;
    ///                 effect is Completed, employee field IS changed, one EffectAttempt row exists.
    ///   3. Re-drain → idempotent; terminal row not re-claimed; count = 0, field unchanged.
    /// </summary>
    [Fact]
    public async Task Date_effective_field_update_runs_once_on_the_worker_after_approval()
    {
        // Arrange: employee starts with phone "0500000000"; we want to change it to "0555000999"
        // with an effective date one day in the past (immediately due).
        await using var h = await DeferredPilotHarness.CreateAsync(
            field: "phone",
            newValue: "0555000999",
            effectiveOn: DateTime.UtcNow.AddDays(-1));

        // ── Act 1: run CompletionEngine (simulate approval completing) ───────
        var completion = await h.Engine.ExecuteAsync(h.RequestInstanceId, default);

        // Assert: the engine succeeded (it persisted the deferred effect, not executed it)
        completion.Success.Should().BeTrue("engine must succeed when enqueueing a deferred effect");

        // The run is waiting for the drainer
        (await h.RunStatus()).Should().Be(CompletionRunStatus.AwaitingDeferred,
            "run must be AwaitingDeferred while at least one deferred effect is pending");

        // The employee field must NOT have been changed yet
        (await h.EmployeePhone()).Should().Be("0500000000",
            "deferred effect must not execute inline — the field must still hold the original value");

        // ── Act 2: run the drainer ────────────────────────────────────────────
        var processed = await h.Drainer.DrainAsync(default);

        // Exactly one effect was processed
        processed.Should().Be(1, "exactly one due deferred effect exists");

        // Effect is now terminal
        (await h.EffectStatus()).Should().Be(CompletionEffectStatus.Completed,
            "the deferred effect must be Completed after a successful drain");

        // The employee field is now changed
        (await h.EmployeePhone()).Should().Be("0555000999",
            "EmployeeUpdateFieldExecutor must have written the new phone value to the Employee row");

        // One EffectAttempt row recorded
        (await h.AttemptCount()).Should().Be(1,
            "exactly one EffectAttempt row must have been written for the single successful drain");

        // ── Act 3: drain again (idempotency check) ───────────────────────────
        var reprocessed = await h.Drainer.DrainAsync(default);

        reprocessed.Should().Be(0,
            "a Completed (terminal) effect must not be re-claimed by a subsequent drain");

        // Field is still the updated value — nothing reverted it
        (await h.EmployeePhone()).Should().Be("0555000999",
            "the employee field must remain unchanged after the second (no-op) drain");

        // Still only one attempt
        (await h.AttemptCount()).Should().Be(1,
            "no additional EffectAttempt must be written during the idempotent second drain");
    }
}
