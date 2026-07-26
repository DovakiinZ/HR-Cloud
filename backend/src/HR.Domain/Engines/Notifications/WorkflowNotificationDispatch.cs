using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Notifications;

/// <summary>Deterministic delivery ledger. One row per (request, event, step, rule, user) guarantees
/// a replayed transition delivers a notification at most once. StepOrder uses -1 when step-agnostic
/// so the composite key stays non-null.</summary>
public class WorkflowNotificationDispatch : TenantEntity
{
    public Guid RequestInstanceId { get; set; }
    public WorkflowNotificationEvent Event { get; set; }
    public int StepOrder { get; set; }
    public Guid RuleId { get; set; }
    public Guid UserId { get; set; }
    public DateTime DispatchedAt { get; set; }
}
