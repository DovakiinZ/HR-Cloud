using FluentAssertions;
using HR.Application.Common.Models;
using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Completion;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Timeline;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Completion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

// ── Fakes ─────────────────────────────────────────────────────────────────────

file sealed class RecoveryTestCurrentUser : HR.Application.Common.Interfaces.ICurrentUserService
{
    public Guid UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public string? Email => "recovery@hr.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

/// <summary>Records every PublishEvent call for assertion.</summary>
file sealed class RecordingTimelineEngine : ITimelineEngine
{
    public record PublishedCall(string Category, string EntityType, Guid EntityId, string Action,
        string? DescriptionEn, string? DescriptionAr, object? Metadata);

    public List<PublishedCall> Calls { get; } = new();

    public Task PublishEvent(string category, string entityType, Guid entityId, string action,
        string? descriptionEn = null, string? descriptionAr = null, object? metadata = null,
        CancellationToken ct = default)
    {
        Calls.Add(new PublishedCall(category, entityType, entityId, action, descriptionEn, descriptionAr, metadata));
        return Task.CompletedTask;
    }

    public Task<PaginatedList<TimelineEvent>> GetTimeline(string entityType, Guid entityId,
        int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
        => Task.FromResult(new PaginatedList<TimelineEvent>());
}

// ── Harness ───────────────────────────────────────────────────────────────────

file sealed class RecoveryHarness : IAsyncDisposable
{
    public ApplicationDbContext Db { get; }
    public RecordingTimelineEngine Timeline { get; } = new();
    public Guid TenantId { get; }

    private RecoveryHarness(ApplicationDbContext db, Guid tenantId)
    { Db = db; TenantId = tenantId; }

    public static async Task<RecoveryHarness> CreateAsync()
    {
        var user = new RecoveryTestCurrentUser();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            user);

        return new RecoveryHarness(db, user.TenantId);
    }

    /// <summary>Seeds a RequestType → RequestInstance → CompletionRun and returns the run.</summary>
    public async Task<(CompletionRun run, Guid instanceId)> SeedRunAsync()
    {
        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "REC_TEST",
            NameEn = "Recovery Test",
            NameAr = "اختبار الاسترداد",
            FormDefinitionId = Guid.NewGuid(),
            IsActive = true,
        };
        Db.Set<RequestType>().Add(requestType);

        var instance = new RequestInstance
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            RequestTypeId = requestType.Id,
            RequestNumber = "REQ-REC-001",
            EmployeeId = Guid.NewGuid(),
            FormSubmissionId = Guid.NewGuid(),
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
        };
        Db.Set<RequestInstance>().Add(instance);

        var run = new CompletionRun
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            RequestInstanceId = instance.Id,
            Status = CompletionRunStatus.AwaitingDeferred,
            StartedAt = DateTime.UtcNow,
        };
        Db.CompletionRuns.Add(run);
        await Db.SaveChangesAsync();

        return (run, instance.Id);
    }

    public CompletionEffect AddEffect(CompletionRun run, string effectType,
        CompletionEffectStatus status, int maxAttempts = 3, int attempts = 1,
        string? failureReason = null, string? leasedBy = null, DateTime? leasedUntil = null)
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
            MaxAttempts = maxAttempts,
            Attempts = attempts,
            FailureReason = failureReason,
            LeasedBy = leasedBy,
            LeasedUntil = leasedUntil,
            IdempotencyKey = Guid.NewGuid().ToString(),
        };
        Db.CompletionEffects.Add(effect);
        return effect;
    }

    public IScheduledEffectRecoveryService BuildService()
        => new ScheduledEffectRecoveryService(Db, Timeline);

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ScheduledEffectRecoveryServiceTests
{
    /// <summary>
    /// Fact 1: ListAttentionAsync returns only ManualReview and Failed effects —
    /// Completed and Scheduled rows are excluded.
    /// </summary>
    [Fact]
    public async Task Lists_only_manual_review_and_failed_deferred_effects()
    {
        await using var h = await RecoveryHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();

        h.AddEffect(run, "Type.ManualReview", CompletionEffectStatus.ManualReview);
        h.AddEffect(run, "Type.Failed", CompletionEffectStatus.Failed);
        h.AddEffect(run, "Type.Completed", CompletionEffectStatus.Completed);
        h.AddEffect(run, "Type.Scheduled", CompletionEffectStatus.Scheduled);
        await h.Db.SaveChangesAsync();

        var svc = h.BuildService();
        var result = await svc.ListAttentionAsync(CancellationToken.None);

        result.Should().HaveCount(2, "only ManualReview and Failed rows need attention");
        result.Select(r => r.EffectType).Should()
            .BeEquivalentTo(new[] { "Type.ManualReview", "Type.Failed" });
    }

    /// <summary>
    /// Fact 2: RetryAsync on a ManualReview effect resets it to Pending,
    /// clears the lease, sets NextAttemptAt ≤ now, clears FailureReason, and returns true.
    /// </summary>
    [Fact]
    public async Task Retry_resets_a_manual_review_effect_to_pending()
    {
        await using var h = await RecoveryHarness.CreateAsync();
        var (run, instanceId) = await h.SeedRunAsync();

        var leaseExpiry = DateTime.UtcNow.AddMinutes(5);
        // Seed with Attempts == MaxAttempts to represent a genuinely-exhausted effect
        // (this is exactly the state the drainer leaves it in before ManualReview).
        var effect = h.AddEffect(run, "Type.ToRetry", CompletionEffectStatus.ManualReview,
            maxAttempts: 5, attempts: 5,
            failureReason: "previous error", leasedBy: "worker-1", leasedUntil: leaseExpiry);
        await h.Db.SaveChangesAsync();

        var before = DateTime.UtcNow;
        var svc = h.BuildService();
        var result = await svc.RetryAsync(effect.Id, CancellationToken.None);

        result.Should().BeTrue();

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.Pending, "ManualReview → Pending on retry");
        row.NextAttemptAt.Should().NotBeNull("NextAttemptAt must be set so the worker picks it up");
        row.NextAttemptAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(1), "NextAttemptAt must be now or past");
        row.LeasedBy.Should().BeNull("lease must be cleared");
        row.LeasedUntil.Should().BeNull("lease expiry must be cleared");
        row.FailureReason.Should().BeNull("failure reason must be cleared on retry");
        row.Attempts.Should().Be(0, "attempt budget must be reset so the drainer will re-claim the effect");
        (row.Attempts < row.MaxAttempts).Should().BeTrue("effect must satisfy the drainer's re-claim condition after retry");

        h.Timeline.Calls.Should().ContainSingle(c => c.Action == "EffectRequeued" && c.EntityId == instanceId,
            "an EffectRequeued timeline event must be published");
    }

    /// <summary>
    /// Fact 3: RetryAsync returns false for a Completed effect (not resettable).
    /// </summary>
    [Fact]
    public async Task Retry_returns_false_for_a_completed_effect()
    {
        await using var h = await RecoveryHarness.CreateAsync();
        var (run, _) = await h.SeedRunAsync();

        var effect = h.AddEffect(run, "Type.Done", CompletionEffectStatus.Completed);
        await h.Db.SaveChangesAsync();

        var svc = h.BuildService();
        var result = await svc.RetryAsync(effect.Id, CancellationToken.None);

        result.Should().BeFalse("a Completed effect must not be resettable");
        h.Timeline.Calls.Should().BeEmpty("no timeline event should be published for a no-op");
    }

    /// <summary>
    /// Fact 4: SkipAsync marks a ManualReview effect as Skipped with the supplied reason,
    /// clears the lease, and publishes an EffectSkipped timeline event.
    /// </summary>
    [Fact]
    public async Task Skip_marks_the_effect_skipped_with_reason()
    {
        await using var h = await RecoveryHarness.CreateAsync();
        var (run, instanceId) = await h.SeedRunAsync();

        var leaseExpiry = DateTime.UtcNow.AddMinutes(5);
        var effect = h.AddEffect(run, "Type.ToSkip", CompletionEffectStatus.ManualReview,
            leasedBy: "worker-2", leasedUntil: leaseExpiry);
        await h.Db.SaveChangesAsync();

        const string SkipReason = "no longer applicable after HR override";
        var svc = h.BuildService();
        var result = await svc.SkipAsync(effect.Id, SkipReason, CancellationToken.None);

        result.Should().BeTrue();

        var row = await h.Db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id);
        row.Status.Should().Be(CompletionEffectStatus.Skipped);
        row.FailureReason.Should().Be(SkipReason);
        row.LeasedBy.Should().BeNull("lease must be cleared on skip");
        row.LeasedUntil.Should().BeNull();

        h.Timeline.Calls.Should().ContainSingle(c => c.Action == "EffectSkipped" && c.EntityId == instanceId,
            "an EffectSkipped timeline event must be published");
    }
}
