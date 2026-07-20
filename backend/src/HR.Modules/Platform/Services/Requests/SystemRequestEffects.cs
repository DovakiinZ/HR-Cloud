using HR.Application.Engines.Completion;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Requests;

/// <param name="Inputs">Input key → mapping, exactly as it will be stored in ConfigurationJson.</param>
public sealed record RequiredEffectSpec(
    string EffectType,
    EffectTrigger Trigger,
    EffectExecutionMode ExecutionMode,
    Dictionary<string, EffectValueMapping> Inputs,
    int EffectVersion = 1);

/// <summary>
/// The effects a system request must keep, per request code.
///
/// "Required" means another part of the product depends on it. Approving a Leave Request has to
/// deduct the leave balance and mark the attendance days, or payroll and attendance quietly diverge
/// from what HR approved. A tenant may add effects, reorder them, relabel the request and disable
/// anything optional — but not remove these.
///
/// Declared here rather than inside RequestSeeder so provisioning can reconcile them on an upgrade
/// without re-running form and workflow creation.
/// </summary>
public static class SystemRequestEffects
{
    private static EffectValueMapping Ctx(string key) => new() { Source = EffectValueSource.RequestContext, Key = key };
    private static EffectValueMapping Field(string key) => new() { Source = EffectValueSource.FormField, Key = key };
    private static EffectValueMapping Const(string value) => new() { Source = EffectValueSource.Constant, Key = value };

    private static Dictionary<string, EffectValueMapping> Map(params (string Key, EffectValueMapping Mapping)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Mapping, StringComparer.OrdinalIgnoreCase);

    private static RequiredEffectSpec Transactional(string effectType, Dictionary<string, EffectValueMapping> inputs)
        => new(effectType, EffectTrigger.FinalApproval, EffectExecutionMode.Transactional, inputs);

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<RequiredEffectSpec>> Required =
        new Dictionary<string, IReadOnlyList<RequiredEffectSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            // Leave: the balance deduction and the attendance marking are both load-bearing.
            // The leave snapshot lives on the RequestInstance itself, so these bind to request
            // context rather than to form fields — renaming a form field cannot break them.
            ["LEAVE_REQUEST"] = new[]
            {
                Transactional(EffectTypes.LeaveCreateApprovedLeave, Map(
                    ("leaveTypeId", Ctx(RequestContextKeys.LeaveTypeId)),
                    ("startDate", Ctx(RequestContextKeys.StartDate)),
                    ("endDate", Ctx(RequestContextKeys.EndDate)),
                    ("daysCount", Ctx(RequestContextKeys.DaysCount)))),
                Transactional(EffectTypes.AttendanceApplyLeaveDays, Map(
                    ("startDate", Ctx(RequestContextKeys.StartDate)),
                    ("endDate", Ctx(RequestContextKeys.EndDate)))),
            },

            ["MISSING_PUNCH"] = new[]
            {
                Transactional(EffectTypes.AttendanceCreatePunch, Map(
                    ("date", Field("startDate")),
                    ("checkIn", Field("checkIn")),
                    ("checkOut", Field("checkOut")),
                    ("reason", Field("reason")))),
            },

            ["ATTENDANCE_CORRECTION"] = new[]
            {
                Transactional(EffectTypes.AttendanceCorrect, Map(
                    ("date", Field("startDate")),
                    ("reason", Field("reason")))),
            },

            ["OVERTIME_REQUEST"] = new[]
            {
                Transactional(EffectTypes.AttendanceCorrect, Map(
                    ("date", Field("startDate")),
                    ("reason", Field("reason")))),
            },

            ["EXPENSE_CLAIM"] = new[]
            {
                Transactional(EffectTypes.ExpenseCreateClaim, Map(
                    ("expenseCategory", Field("expenseCategory")),
                    ("amount", Field("amount")),
                    ("description", Field("reason")),
                    ("receipt", Field("receipt")))),
            },

            ["LOAN_REQUEST"] = new[]
            {
                Transactional(EffectTypes.LoanCreate, Map(
                    ("loanType", Field("loanType")),
                    ("amount", Field("amount")),
                    ("installmentMonths", Field("installmentMonths")),
                    ("kind", Const("Loan")))),
            },

            // A salary advance is a one-installment loan. The distinction used to be a hardcoded
            // `type.Code == "SALARY_ADVANCE"` inside CompletionEffectFactory; expressing it as
            // configuration is what removes that code-string special case.
            ["SALARY_ADVANCE"] = new[]
            {
                Transactional(EffectTypes.LoanCreate, Map(
                    ("loanType", Field("loanType")),
                    ("amount", Field("amount")),
                    ("installmentMonths", Const("1")),
                    ("kind", Const("Advance")))),
            },

            // Custody: the dynamic example. Asset comes from the form, employee from the request —
            // binding the employee to a form field would let a requester assign someone else's kit.
            ["CUSTODY_REQUEST"] = new[]
            {
                Transactional(EffectTypes.AssetsAssignCustody, Map(
                    ("assetId", Field("assetId")),
                    ("employeeId", Ctx(RequestContextKeys.EmployeeId)),
                    ("expectedReturnDate", Field("expectedReturnDate")),
                    ("notes", Field("notes")))),
            },

            // BUSINESS_TRIP and SALARY_CERTIFICATE are approval-and-document requests: they produce
            // a letter, and touch no business state. No required effects, deliberately — an empty
            // entry here is a decision, not an omission.
            ["BUSINESS_TRIP"] = Array.Empty<RequiredEffectSpec>(),
            ["SALARY_CERTIFICATE"] = Array.Empty<RequiredEffectSpec>(),
        };
}
