using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Application.Engines.Audit;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Timeline;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Completion;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

// ── Fakes ─────────────────────────────────────────────────────────────────────

file sealed class FakeCurrentUser : ICurrentUserService
{
    public Guid UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public string? Email => "test@hr.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

file sealed class FakeTimeline : ITimelineEngine
{
    public Task PublishEvent(string category, string entityType, Guid entityId, string action,
        string? descriptionEn = null, string? descriptionAr = null, object? metadata = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<PaginatedList<TimelineEvent>> GetTimeline(string entityType, Guid entityId,
        int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
        => Task.FromResult(new PaginatedList<TimelineEvent> { PageNumber = 1, PageSize = 20 });
}

file sealed class FakeAudit : IAuditEngine
{
    public Task LogChange(string entityType, Guid entityId, string action, object? oldValues = null,
        object? newValues = null, CancellationToken ct = default)
        => Task.CompletedTask;
}

file sealed class FakeNotifications : INotificationService
{
    public Task NotifyAsync(Guid userId, string titleAr, string titleEn, string bodyAr, string bodyEn,
        string category, Guid? entityId, string link, DateTime? dueAt = null, bool email = true,
        CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// A stub factory that returns a fixed list of intents.
/// </summary>
file sealed class StubFactory : ICompletionEffectFactory
{
    private readonly IReadOnlyList<EffectIntent> _intents;
    public StubFactory(params EffectIntent[] intents) => _intents = intents;
    public Task<IReadOnlyList<EffectIntent>> BuildAsync(Guid requestInstanceId, CancellationToken ct)
        => Task.FromResult(_intents);
}

/// <summary>
/// A stub executor that always succeeds.
/// </summary>
file sealed class SuccessExecutor : IEffectExecutor
{
    public string EffectType { get; }
    public SuccessExecutor(string effectType) => EffectType = effectType;
    public Task<EffectExecutionResult> ExecuteAsync(EffectContext context, CancellationToken ct)
        => Task.FromResult(EffectExecutionResult.Ok(summary: "ok"));
}

/// <summary>
/// A stub executor that always throws.
/// </summary>
file sealed class ThrowingExecutor : IEffectExecutor
{
    public string EffectType { get; }
    public ThrowingExecutor(string effectType) => EffectType = effectType;
    public Task<EffectExecutionResult> ExecuteAsync(EffectContext context, CancellationToken ct)
        => throw new InvalidOperationException("Simulated executor failure");
}

file sealed class StubRegistry : IEffectExecutorRegistry
{
    private readonly Dictionary<string, IEffectExecutor> _map;
    public StubRegistry(params IEffectExecutor[] executors)
        => _map = executors.ToDictionary(e => e.EffectType);

    public IEffectExecutor Resolve(string effectType)
        => _map.TryGetValue(effectType, out var ex) ? ex
            : throw new InvalidOperationException($"No executor for '{effectType}'");

    public bool TryResolve(string effectType, out IEffectExecutor executor)
        => _map.TryGetValue(effectType, out executor!);
}

// ── Harness ───────────────────────────────────────────────────────────────────

file sealed class EngineHarness : IAsyncDisposable
{
    public ApplicationDbContext Db { get; }
    public CompletionEngine Engine { get; }
    public Guid RequestInstanceId { get; }

    private EngineHarness(ApplicationDbContext db, CompletionEngine engine, Guid requestInstanceId)
    {
        Db = db;
        Engine = engine;
        RequestInstanceId = requestInstanceId;
    }

    public static async Task<EngineHarness> CreateAsync(
        StubFactory factory,
        StubRegistry registry)
    {
        var user = new FakeCurrentUser();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            user);

        var tenantId = user.TenantId;

        // Minimal seed: FormDefinition → RequestType → FormSubmission → RequestInstance
        var formDef = new FormDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "ENG_TEST_FORM",
            NameEn = "Engine Test Form",
            NameAr = "نموذج اختبار المحرك",
            Module = "Platform",
        };
        db.Set<FormDefinition>().Add(formDef);

        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "ENG_TEST",
            NameEn = "Engine Test",
            NameAr = "اختبار المحرك",
            FormDefinitionId = formDef.Id,
            IsActive = true,
        };
        db.Set<RequestType>().Add(requestType);

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

        var instance = new RequestInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            RequestNumber = "REQ-ENG-001",
            EmployeeId = Guid.NewGuid(),
            FormSubmissionId = submission.Id,
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
        };
        db.Set<RequestInstance>().Add(instance);
        await db.SaveChangesAsync();

        var engine = new CompletionEngine(
            db, factory, registry,
            new FakeTimeline(), new FakeAudit(), new FakeNotifications(), user);

        return new EngineHarness(db, engine, instance.Id);
    }

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class CompletionEngineDeferredTests
{
    /// <summary>
    /// A run with one Deferred intent must persist the effect as Scheduled or Pending,
    /// set its IdempotencyKey, and set the run status to AwaitingDeferred.
    /// </summary>
    [Fact]
    public async Task Deferred_effect_is_persisted_not_executed_and_run_awaits()
    {
        var intent = new EffectIntent(
            EffectType: "Employee.UpdateField",
            Sequence: 1,
            Payload: "{}",
            Mode: EffectExecutionMode.Deferred,
            MaxAttempts: 3);

        // The executor should never be invoked for a deferred effect.
        var throwingExecutor = new ThrowingExecutor("Employee.UpdateField");
        await using var h = await EngineHarness.CreateAsync(
            new StubFactory(intent),
            new StubRegistry(throwingExecutor));

        var result = await h.Engine.ExecuteAsync(h.RequestInstanceId, default);

        // Overall result must be successful (we enqueued, not failed)
        result.Success.Should().BeTrue();

        // Load the run
        var run = await h.Db.CompletionRuns
            .Include(r => r.Effects)
            .FirstAsync(r => r.RequestInstanceId == h.RequestInstanceId);

        run.Status.Should().Be(CompletionRunStatus.AwaitingDeferred);
        run.CompletedAt.Should().BeNull("run is not complete while deferred effects are pending");

        // The one deferred effect must not be Completed or Executing
        var effect = run.Effects.Single();
        effect.Status.Should().BeOneOf(
            new[] { CompletionEffectStatus.Pending, CompletionEffectStatus.Scheduled });
        effect.IdempotencyKey.Should().NotBeNullOrEmpty("deferred effects get their id as the idempotency key");
        effect.IdempotencyKey.Should().Be(effect.Id.ToString());
    }

    /// <summary>
    /// Mixed run: one Transactional (inline) + one Deferred. The inline effect must complete,
    /// the deferred must remain Pending/Scheduled, and the run must be AwaitingDeferred.
    /// </summary>
    [Fact]
    public async Task Mixed_run_executes_inline_and_defers_the_rest()
    {
        var inlineIntent = new EffectIntent(
            EffectType: "Inline.Action",
            Sequence: 1,
            Payload: "{}",
            Mode: EffectExecutionMode.Transactional);

        var deferredIntent = new EffectIntent(
            EffectType: "Deferred.Action",
            Sequence: 2,
            Payload: "{}",
            Mode: EffectExecutionMode.Deferred,
            MaxAttempts: 2);

        await using var h = await EngineHarness.CreateAsync(
            new StubFactory(inlineIntent, deferredIntent),
            new StubRegistry(
                new SuccessExecutor("Inline.Action"),
                new ThrowingExecutor("Deferred.Action")));  // deferred executor must never be called

        var result = await h.Engine.ExecuteAsync(h.RequestInstanceId, default);

        result.Success.Should().BeTrue();

        var run = await h.Db.CompletionRuns
            .Include(r => r.Effects)
            .FirstAsync(r => r.RequestInstanceId == h.RequestInstanceId);

        run.Status.Should().Be(CompletionRunStatus.AwaitingDeferred);
        run.CompletedAt.Should().BeNull();

        var inlineEffect = run.Effects.Single(e => e.Sequence == 1);
        var deferredEffect = run.Effects.Single(e => e.Sequence == 2);

        inlineEffect.Status.Should().Be(CompletionEffectStatus.Completed,
            "inline Transactional effect must run and complete");

        deferredEffect.Status.Should().BeOneOf(
            new[] { CompletionEffectStatus.Pending, CompletionEffectStatus.Scheduled },
            "deferred effect must not be executed inline");
        deferredEffect.IdempotencyKey.Should().Be(deferredEffect.Id.ToString());
        deferredEffect.MaxAttempts.Should().Be(2);
    }

    /// <summary>
    /// When an inline effect throws, the deferred effects must be Cancelled (not left
    /// runnable). The run lands in Failed.
    /// </summary>
    [Fact]
    public async Task Inline_failure_cancels_pending_deferred_effects()
    {
        var inlineIntent = new EffectIntent(
            EffectType: "Inline.Failing",
            Sequence: 1,
            Payload: "{}",
            Mode: EffectExecutionMode.Transactional);

        var deferredIntent = new EffectIntent(
            EffectType: "Deferred.Waiting",
            Sequence: 2,
            Payload: "{}",
            Mode: EffectExecutionMode.Deferred,
            MaxAttempts: 5);

        await using var h = await EngineHarness.CreateAsync(
            new StubFactory(inlineIntent, deferredIntent),
            new StubRegistry(
                new ThrowingExecutor("Inline.Failing"),
                new ThrowingExecutor("Deferred.Waiting")));

        var result = await h.Engine.ExecuteAsync(h.RequestInstanceId, default);

        result.Success.Should().BeFalse("the inline effect threw, so the run must fail");

        // Re-query after PersistFailureAsync committed its changes
        var run = await h.Db.CompletionRuns
            .Include(r => r.Effects)
            .FirstAsync(r => r.RequestInstanceId == h.RequestInstanceId);

        run.Status.Should().Be(CompletionRunStatus.Failed);

        var inlineEffect = run.Effects.Single(e => e.Sequence == 1);
        var deferredEffect = run.Effects.Single(e => e.Sequence == 2);

        inlineEffect.Status.Should().Be(CompletionEffectStatus.Failed);
        deferredEffect.Status.Should().Be(CompletionEffectStatus.Cancelled,
            "deferred effects must be cancelled when the inline phase fails");
    }
}
