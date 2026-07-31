namespace HR.Application.Engines.Completion;

/// <summary>
/// Canonical effect-type identifiers. The "{Module}.{Action}" convention keys executors to the
/// module that owns them. Adding a future effect is just a new constant + a new executor.
/// </summary>
public static class EffectTypes
{
    // Leave
    public const string LeaveCreateApprovedLeave = "Leave.CreateApprovedLeave";

    // Attendance
    public const string AttendanceApplyLeaveDays = "Attendance.ApplyLeaveDays";
    public const string AttendanceCreatePunch = "Attendance.CreatePunch";
    public const string AttendanceCorrect = "Attendance.Correct";
    public const string AttendanceCreatePermission = "Attendance.CreatePermission";

    // Payroll-adjacent
    public const string ExpenseCreateClaim = "Expense.CreateClaim";
    public const string LoanCreate = "Loan.Create";

    // Assets
    public const string AssetsAssignCustody = "Assets.AssignCustody";

    // Employees. Updates a whitelisted self-service profile field (contact/address/emergency)
    // with an audited before/after — never an arbitrary column.
    public const string EmployeeUpdateField = "Employee.UpdateField";

    // Cross-cutting. Notification.Send is asynchronous: its executor only enqueues, so a mail
    // transport being down can never roll back an approved leave.
    public const string NotificationSend = "Notification.Send";
    public const string TaskCreate = "Task.Create";
}
