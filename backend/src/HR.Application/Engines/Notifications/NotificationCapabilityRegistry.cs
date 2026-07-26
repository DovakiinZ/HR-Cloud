using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

/// <summary>The single source of truth for what the framework can actually do today. Validation
/// rejects rules referencing anything outside these sets; the future admin API lists only these.</summary>
public static class NotificationCapabilityRegistry
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxRecipients = 20;

    public static readonly IReadOnlySet<WorkflowNotificationEvent> SupportedEvents = new HashSet<WorkflowNotificationEvent>
    {
        WorkflowNotificationEvent.Submitted,
        WorkflowNotificationEvent.StepAssigned,
        WorkflowNotificationEvent.StepApproved,
        WorkflowNotificationEvent.Rejected,
        WorkflowNotificationEvent.Returned,
        WorkflowNotificationEvent.FinalApproved,
    };

    public static readonly IReadOnlySet<NotificationRecipientType> SupportedRecipientTypes = new HashSet<NotificationRecipientType>
    {
        NotificationRecipientType.Requester, NotificationRecipientType.EmployeeConcerned,
        NotificationRecipientType.CurrentApprover, NotificationRecipientType.PreviousApprover,
        NotificationRecipientType.DirectManager, NotificationRecipientType.DepartmentManager,
        NotificationRecipientType.SpecificEmployee, NotificationRecipientType.Role,
        NotificationRecipientType.HrTeam, NotificationRecipientType.FinanceTeam,
        NotificationRecipientType.StepAssignees,
    };

    /// <summary>Recipient types that need a refId (an entity reference) to resolve.</summary>
    public static bool RequiresRefId(NotificationRecipientType type) =>
        type is NotificationRecipientType.SpecificEmployee or NotificationRecipientType.Role;
}
