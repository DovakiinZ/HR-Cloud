using HR.Domain.Engines.Completion;
using HR.Domain.Enums;

namespace HR.Application.Engines.Completion;

/// <summary>Pure: applies an execution outcome to a deferred completion-effect row. No I/O — the drainer
/// owns persistence. Mirrors the shape of EmailDeliveryDecision.</summary>
public static class ScheduledEffectDecision
{
    public static void ApplySuccess(CompletionEffect row, EffectExecutionResult result, DateTime nowUtc)
    {
        row.Status = result.IsSkipped ? CompletionEffectStatus.Skipped : CompletionEffectStatus.Completed;
        if (result.IsSkipped) row.FailureReason = result.SkipReason;
        row.ExecutedAt = nowUtc;
        row.TargetEntityType = result.TargetEntityType;
        row.TargetRecordId = result.TargetRecordId;
        ClearLease(row);
    }

    public static void ApplyFailure(
        CompletionEffect row, string error, bool permanent, DateTime nowUtc,
        TimeSpan baseBackoff, TimeSpan maxBackoff)
    {
        row.FailureReason = Truncate(error, 2000);
        row.ExecutedAt = nowUtc;
        ClearLease(row);

        if (permanent || row.Attempts >= row.MaxAttempts)
        {
            row.Status = CompletionEffectStatus.ManualReview;
            row.NextAttemptAt = null;
            return;
        }

        row.Status = CompletionEffectStatus.Retrying;
        var factor = Math.Pow(2, Math.Max(0, row.Attempts - 1));
        var delay = TimeSpan.FromTicks((long)Math.Min(baseBackoff.Ticks * factor, maxBackoff.Ticks));
        row.NextAttemptAt = nowUtc.Add(delay);
    }

    private static void ClearLease(CompletionEffect row) { row.LeasedBy = null; row.LeasedUntil = null; }
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
