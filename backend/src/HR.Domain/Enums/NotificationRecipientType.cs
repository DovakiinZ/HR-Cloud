namespace HR.Domain.Enums;

/// <summary>Who a notification rule targets. Values 1-11 are resolved today (see
/// NotificationCapabilityRegistry.SupportedRecipientTypes); 12-13 are reserved and rejected by
/// validation until their resolver lands.</summary>
public enum NotificationRecipientType
{
    Requester = 1,
    EmployeeConcerned = 2,
    CurrentApprover = 3,
    PreviousApprover = 4,
    DirectManager = 5,
    DepartmentManager = 6,
    SpecificEmployee = 7,
    Role = 8,
    HrTeam = 9,
    FinanceTeam = 10,
    StepAssignees = 11,
    FormSelectedEmployee = 12,
    Custom = 13,
}
