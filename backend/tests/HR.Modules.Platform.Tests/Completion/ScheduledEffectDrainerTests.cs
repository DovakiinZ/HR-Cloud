using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Forms;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Completion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

// ── Fakes shared by drainer tests ──────────────────────────────────────────────

file sealed class FakeDrainerCurrentUser : ICurrentUserService
{
    public Guid UserId => Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public Guid TenantId => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public string? Email => "drainer@hr.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

/// <summary>Background context that is a no-op; all gets return default values.</summary>
file sealed class FakeBackground : IBackgroundExecutionContext
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

/// <summary>Executor that always succeeds.</summary>
file sealed class OkExecutor : IEffectExecutor
{
    public string EffectType { get; }
    public OkExecutor(string effectType) => EffectType = effectType;
    public Task<EffectExecutionResult> ExecuteAsync(EffectContext context, CancellationToken ct)
        => Task.FromResult(EffectExecutionResult.Ok(summary: "ok"));
}

/// <summary>Executor that always throws a transient exception.</summary>
file sealed class AlwaysThrowingExecutor : IEffectExecutor
{
    public string EffectType { get; }
    public AlwaysThrowingExecutor(string effectType) => EffectType = effectType;
    public Task<EffectExecutionResult> ExecuteAsync(EffectContext context, CancellationToken ct)
        => throw new InvalidOperationException("Transient failure");
}

/// <summary>Executor that throws a permanent (non-retryable) exception.</summary>
file sealed class PermanentFailExecutor : IEffectExecutor
{
    public string EffectType { get; }
    public PermanentFailExecutor(string effectType) => EffectType = effectType;
    public Task<EffectExecutionResult> ExecuteAsync(EffectContext context, CancellationToken ct)
        => throw new NonRetryableEffectException("Permanent failure");
}

file sealed class DrainerStubRegistry : IEffectExecutorRegistry
{
    private readonly Dictionary<string, IEffectExecutor> _map;
    public DrainerStubRegistry(params IEffectExecutor[] executors)
        => _map = executors.ToDictionary(e => e.EffectType);

    public IEffectExecutor Resolve(string effectType)
        => _map.TryGetValue(effectType, out var ex) ? ex
            : throw new InvalidOperationException($"No executor for '{effectType}'");

    public bool TryResolve(string effectType, out IEffectExecutor executor)
        => _map.TryGetValue(effectType, out executor!);
}

// ── Harness ───────────────────────────────────────────────────────────────────

file sealed class DrainerHarness : IAsyncDisposable
{
    public ApplicationDbContext Db { get; }
    public Guid TenantId { get; }

    public DrainerHarness(ApplicationDbContext db, Guid tenantId)
    { Db = db; TenantId = tenantId; }

    public static async Task<DrainerHarness> CreateAsync()
    {
        var user = new FakeDrainerCurrentUser();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            user);

        return new DrainerHarness(db, user.TenantId);
    }

    /// <summary>Seeds the minimum required rows: RequestType → RequestInstance, and returns
    /// a factory-like helper for adding CompletionEffect rows to the given run.</summary>
    public async Task<(CompletionRun run, Guid instanceId)> SeedRunAsync()
    {
        var tenantId = TenantId;

        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "DRN_TEST",
            NameEn = "Drainer Test",
            NameAr = "اختبار الدريناتر",
            FormDefinitionId = Guid.NewGuid(), // not needed for drainer tests
            IsActive = true,
        };
        Db.Set<RequestType>().Add(requestType);

        var instance = new RequestInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            RequestNumber = "REQ-DRN-001",
            EmployeeId = Guid.NewGuid(),
            FormSubmissionId = Guid.NewGuid(),
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
        };
        Db.Set<RequestInstance>().Add(instance);

        var run = new CompletionRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestInstanceId = instance.Id,
            Status = CompletionRunStatus.AwaitingDeferred,
            StartedAt = DateTime.UtcNow,
        };
        Db.CompletionRuns.Add(run);
        await Db.SaveChangesAsync();

        return (run, instance.Id);
    }

    public CompletionEffect AddEffect(CompletionRun run, string effectType,
        CompletionEffectStatus status = CompletionEffectStatus.Pending,
        DateTime? scheduledFor = null,
        int maxAttempts = 3,
        DateTime? nextAttemptAt = null)
    {
        var effect = new CompletionEffect
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CompletionRunId = run.Id,
            RequestInstanceId = run.RequestInstanceId,
            EffectType = effectType,
            Sequence = 1,
            Payload = "{}",
            Status = status,
            ScheduledFor = scheduledFor,
            MaxAttempts = maxAttempts,
            NextAttemptAt = nextAttemptAt,
            IdempotencyKey = Guid.NewGuid().ToString(),
        };
        Db.CompletionEffects.Add(effect);
        return effect;
    }

    public ScheduledEffectDrainer BuildDrainer(IEffectExecutorRegistry registry, Func<DateTime>? clock = null)
        => new(Db, registry, new FakeBackground(),
            NullLogger<ScheduledEffectDrainer>.Instance, clock);

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ScheduledEffectDrainerTests
{
    /// <summary>
    /// Fact 1: A due Pending effect is executed once and transitions to Completed.
    /// A second DrainAsync call is idempotent (already terminal, not picked up again).
    /// </summary>
    [Fact]
    public async Task Runs_a_due_pending_effect_once_and_marks_completed()
    {
        await using var h = await DrainerHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();
        var effect = h.AddEffect(run, "Test.Effect", CompletionEffectStatus.Pending);
        await h.Db.SaveChangesAsync();

        var registry = new DrainerStubRegistry(new OkExecutor("Test.Effect"));
        var now = DateTime.UtcNow;
        var drainer = h.BuildDrainer(registry, () => now);

        var count1 = await drainer.DrainAsync(CancellationToken.None);
        count1.Should().Be(1);

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.Completed);

        // Second drain: terminal row must not be re-processed
        var count2 = await drainer.DrainAsync(CancellationToken.None);
        count2.Should().Be(0, "a Completed effect must not be picked up again");
    }

    /// <summary>
    /// Fact 2: A Scheduled effect whose ScheduledFor is in the future is NOT run.
    /// </summary>
    [Fact]
    public async Task Does_not_run_a_future_scheduled_effect()
    {
        await using var h = await DrainerHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();
        var future = DateTime.UtcNow.AddHours(1);
        var effect = h.AddEffect(run, "Test.Future", CompletionEffectStatus.Scheduled, scheduledFor: future);
        await h.Db.SaveChangesAsync();

        var registry = new DrainerStubRegistry(new OkExecutor("Test.Future"));
        var now = DateTime.UtcNow; // now < scheduledFor
        var drainer = h.BuildDrainer(registry, () => now);

        var count = await drainer.DrainAsync(CancellationToken.None);
        count.Should().Be(0, "a future-scheduled effect must not be run before its date");

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.Scheduled, "status must be unchanged");
    }

    /// <summary>
    /// Fact 3: A Scheduled effect whose ScheduledFor is in the past IS run.
    /// </summary>
    [Fact]
    public async Task Runs_a_scheduled_effect_once_its_date_passes()
    {
        await using var h = await DrainerHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();
        var past = DateTime.UtcNow.AddHours(-1);
        var effect = h.AddEffect(run, "Test.Past", CompletionEffectStatus.Scheduled, scheduledFor: past);
        await h.Db.SaveChangesAsync();

        var registry = new DrainerStubRegistry(new OkExecutor("Test.Past"));
        var now = DateTime.UtcNow; // now > scheduledFor
        var drainer = h.BuildDrainer(registry, () => now);

        var count = await drainer.DrainAsync(CancellationToken.None);
        count.Should().Be(1);

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.Completed);
    }

    /// <summary>
    /// Fact 4: An always-throwing executor with MaxAttempts=2:
    ///   - First drain: Retrying (attempt 1, NextAttemptAt set).
    ///   - After clearing NextAttemptAt (simulating time passing): second drain → ManualReview.
    /// </summary>
    [Fact]
    public async Task Failing_executor_moves_to_retrying_then_manual_review()
    {
        await using var h = await DrainerHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();
        var effect = h.AddEffect(run, "Test.Fail", CompletionEffectStatus.Pending, maxAttempts: 2);
        await h.Db.SaveChangesAsync();

        var registry = new DrainerStubRegistry(new AlwaysThrowingExecutor("Test.Fail"));
        var now = DateTime.UtcNow;
        var drainer = h.BuildDrainer(registry, () => now);

        // First drain: attempt 1 → Retrying
        var count1 = await drainer.DrainAsync(CancellationToken.None);
        count1.Should().Be(1);

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.Retrying, "first failure should move to Retrying");
        row.Attempts.Should().Be(1);
        row.NextAttemptAt.Should().NotBeNull("a retry backoff window must be set");

        // Simulate time passing: clear the retry gate
        row.NextAttemptAt = null;
        await h.Db.SaveChangesAsync();

        // Second drain: attempt 2 → ManualReview (MaxAttempts exhausted)
        var count2 = await drainer.DrainAsync(CancellationToken.None);
        count2.Should().Be(1);

        row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.ManualReview, "exhausted retries must go to ManualReview");
        row.Attempts.Should().Be(2);
    }

    /// <summary>
    /// Fact 5: A NonRetryableEffectException goes straight to ManualReview on the first attempt.
    /// </summary>
    [Fact]
    public async Task Permanent_failure_skips_retries()
    {
        await using var h = await DrainerHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();
        var effect = h.AddEffect(run, "Test.Perm", CompletionEffectStatus.Pending, maxAttempts: 5);
        await h.Db.SaveChangesAsync();

        var registry = new DrainerStubRegistry(new PermanentFailExecutor("Test.Perm"));
        var now = DateTime.UtcNow;
        var drainer = h.BuildDrainer(registry, () => now);

        var count = await drainer.DrainAsync(CancellationToken.None);
        count.Should().Be(1);

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.ManualReview,
            "NonRetryableEffectException must bypass retry logic and go straight to ManualReview");
        row.Attempts.Should().Be(1, "exactly one attempt must have been made");
        row.NextAttemptAt.Should().BeNull("no retry should be scheduled after a permanent failure");
    }

    /// <summary>
    /// Fact 6: An EffectAttempt row is written per attempt with AttemptNumber matching effect.Attempts.
    /// </summary>
    [Fact]
    public async Task Writes_an_effect_attempt_row_per_attempt()
    {
        await using var h = await DrainerHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();
        var effect = h.AddEffect(run, "Test.AttemptLog", CompletionEffectStatus.Pending, maxAttempts: 3);
        await h.Db.SaveChangesAsync();

        var registry = new DrainerStubRegistry(new AlwaysThrowingExecutor("Test.AttemptLog"));
        var now = DateTime.UtcNow;
        var drainer = h.BuildDrainer(registry, () => now);

        // First attempt
        await drainer.DrainAsync(CancellationToken.None);

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        var attempts = await h.Db.EffectAttempts.IgnoreQueryFilters()
            .Where(a => a.CompletionEffectId == effect.Id)
            .ToListAsync();

        attempts.Should().HaveCount(1, "one EffectAttempt row per drain call");
        attempts[0].AttemptNumber.Should().Be(row.Attempts,
            "AttemptNumber on the log row must equal effect.Attempts after execution");

        // Second attempt (clear retry gate)
        row.NextAttemptAt = null;
        await h.Db.SaveChangesAsync();
        await drainer.DrainAsync(CancellationToken.None);

        attempts = await h.Db.EffectAttempts.IgnoreQueryFilters()
            .Where(a => a.CompletionEffectId == effect.Id)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync();

        attempts.Should().HaveCount(2, "two EffectAttempt rows after two drain calls");
        attempts[1].AttemptNumber.Should().Be(2);
    }
}
