namespace HR.Domain.Enums;

/// <summary>Request-lifecycle events a notification rule can subscribe to. Values 1-6 are dispatched
/// today (see NotificationCapabilityRegistry.SupportedEvents); 7-12 are defined for forward
/// compatibility and are rejected by validation / hidden from APIs until their SP lands.</summary>
public enum WorkflowNotificationEvent
{
    Submitted = 1,
    StepAssigned = 2,
    StepApproved = 3,
    Rejected = 4,
    Returned = 5,
    FinalApproved = 6,
    MoreInfoRequested = 7,
    EffectExecuted = 8,
    EffectFailed = 9,
    Cancelled = 10,
    SlaReminder = 11,
    EscalationTriggered = 12,
}
