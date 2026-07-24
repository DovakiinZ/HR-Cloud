using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Completion;

/// <summary>One recorded attempt to execute a deferred completion effect: which attempt number, when it
/// started, how it ended, and why it failed. Gives a full audit trail across retries.</summary>
public class EffectAttempt : TenantEntity
{
    public Guid CompletionEffectId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public CompletionEffectStatus Status { get; set; }
    public int? DurationMs { get; set; }
    public string? FailureReason { get; set; }

    public CompletionEffect Effect { get; set; } = null!;
}
