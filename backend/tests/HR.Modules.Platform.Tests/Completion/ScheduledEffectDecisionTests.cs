using FluentAssertions;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Completion;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

public class ScheduledEffectDecisionTests
{
    private static readonly DateTime Now = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Base = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(60);

    [Fact]
    public void Success_marks_completed_and_clears_lease()
    {
        var row = new CompletionEffect { Attempts = 1, MaxAttempts = 5, LeasedBy = "w1", LeasedUntil = Now };
        ScheduledEffectDecision.ApplySuccess(row, EffectExecutionResult.Ok(targetEntityType: "Employee"), Now);
        row.Status.Should().Be(CompletionEffectStatus.Completed);
        row.ExecutedAt.Should().Be(Now);
        row.LeasedBy.Should().BeNull();
        row.LeasedUntil.Should().BeNull();
        row.TargetEntityType.Should().Be("Employee");
    }

    [Fact]
    public void Skip_marks_skipped_with_reason()
    {
        var row = new CompletionEffect { Attempts = 1, MaxAttempts = 5 };
        ScheduledEffectDecision.ApplySuccess(row, EffectExecutionResult.Skip(EffectSkipReasons.NothingToDo), Now);
        row.Status.Should().Be(CompletionEffectStatus.Skipped);
        row.FailureReason.Should().Be(EffectSkipReasons.NothingToDo);
    }

    [Fact]
    public void Failure_below_cap_schedules_exponential_backoff_retry()
    {
        var row = new CompletionEffect { Attempts = 2, MaxAttempts = 5, LeasedBy = "w1", LeasedUntil = Now };
        ScheduledEffectDecision.ApplyFailure(row, "boom", permanent: false, Now, Base, Max);
        row.Status.Should().Be(CompletionEffectStatus.Retrying);
        row.NextAttemptAt.Should().Be(Now.AddMinutes(2)); // 1min * 2^(2-1)
        row.LeasedBy.Should().BeNull();
        row.FailureReason.Should().Be("boom");
    }

    [Fact]
    public void Failure_at_cap_goes_to_manual_review()
    {
        var row = new CompletionEffect { Attempts = 5, MaxAttempts = 5 };
        ScheduledEffectDecision.ApplyFailure(row, "boom", permanent: false, Now, Base, Max);
        row.Status.Should().Be(CompletionEffectStatus.ManualReview);
        row.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Permanent_failure_goes_straight_to_manual_review()
    {
        var row = new CompletionEffect { Attempts = 1, MaxAttempts = 5 };
        ScheduledEffectDecision.ApplyFailure(row, "bad config", permanent: true, Now, Base, Max);
        row.Status.Should().Be(CompletionEffectStatus.ManualReview);
    }

    [Fact]
    public void Backoff_is_capped()
    {
        var row = new CompletionEffect { Attempts = 10, MaxAttempts = 20 };
        ScheduledEffectDecision.ApplyFailure(row, "boom", permanent: false, Now, Base, Max);
        row.NextAttemptAt.Should().Be(Now.Add(Max));
    }
}
