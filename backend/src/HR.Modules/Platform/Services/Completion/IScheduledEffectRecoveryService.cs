namespace HR.Modules.Platform.Services.Completion;

/// <summary>Projection returned by the attention list — effects that need a human operator.</summary>
public sealed record AttentionEffectDto(
    Guid Id,
    Guid RequestInstanceId,
    string EffectType,
    int Attempts,
    int MaxAttempts,
    string? FailureReason,
    DateTime? ScheduledFor);

/// <summary>
/// Operator recovery surface for deferred effects that need human intervention:
/// list effects in <c>ManualReview</c> or <c>Failed</c> status, retry them, or skip them.
/// </summary>
public interface IScheduledEffectRecoveryService
{
    /// <summary>Returns all effects with status <c>ManualReview</c> or <c>Failed</c>, ordered by ExecutedAt.</summary>
    Task<IReadOnlyList<AttentionEffectDto>> ListAttentionAsync(CancellationToken ct);

    /// <summary>
    /// Resets a <c>ManualReview</c> or <c>Failed</c> effect back to <c>Pending</c> so the drainer
    /// will pick it up again. Clears the lease, sets <c>NextAttemptAt = now</c>, and publishes
    /// an <c>EffectRequeued</c> timeline event.
    /// Returns <c>false</c> if the effect is not in a recoverable state (e.g. already <c>Completed</c>).
    /// </summary>
    Task<bool> RetryAsync(Guid effectId, CancellationToken ct);

    /// <summary>
    /// Marks a <c>ManualReview</c> or <c>Failed</c> effect as <c>Skipped</c> with the supplied
    /// <paramref name="reason"/> and publishes an <c>EffectSkipped</c> timeline event.
    /// Returns <c>false</c> if the effect is not in a recoverable state.
    /// </summary>
    Task<bool> SkipAsync(Guid effectId, string reason, CancellationToken ct);
}
