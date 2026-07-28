using HR.Application.Engines.Notifications;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Requests;

public sealed record SeededRule(
    string SystemKey, WorkflowNotificationEvent Event, int? StepOrder,
    IReadOnlyList<RecipientSpec> Recipients,
    string SubjectAr, string SubjectEn, string BodyAr, string BodyEn);

/// <summary>Product-default workflow notification rules per request code. Mirrors SystemRequestEffects:
/// declared here so provisioning can reconcile them on a SeedVersion bump. Seeded rows are marked
/// system-owned and are never overwritten once a tenant customizes them.</summary>
public static class SystemWorkflowNotificationRules
{
    private static RecipientSpec R(NotificationRecipientType t) => new(t);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SeededRule>> Rules =
        new Dictionary<string, IReadOnlyList<SeededRule>>(StringComparer.OrdinalIgnoreCase)
        {
            ["LEAVE_REQUEST"] = new[]
            {
                new SeededRule("LEAVE_REQUEST:Submitted:Requester", WorkflowNotificationEvent.Submitted, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم استلام طلب الإجازة", "Leave request received",
                    "تم استلام طلب إجازتك رقم {{Request.Number}} وهو قيد المراجعة.",
                    "Your leave request {{Request.Number}} was received and is under review."),
                new SeededRule("LEAVE_REQUEST:StepAssigned:CurrentApprover", WorkflowNotificationEvent.StepAssigned, null,
                    new[] { R(NotificationRecipientType.CurrentApprover) },
                    "طلب إجازة بانتظار موافقتك", "A leave request needs your approval",
                    "طلب إجازة رقم {{Request.Number}} من {{Employee.FullName}} بانتظار موافقتك.",
                    "Leave request {{Request.Number}} from {{Employee.FullName}} awaits your approval."),
                new SeededRule("LEAVE_REQUEST:Rejected:Requester", WorkflowNotificationEvent.Rejected, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم رفض طلب الإجازة", "Leave request rejected",
                    "نأسف لإبلاغك برفض طلب إجازتك رقم {{Request.Number}}.",
                    "Your leave request {{Request.Number}} was rejected."),
                new SeededRule("LEAVE_REQUEST:Returned:Requester", WorkflowNotificationEvent.Returned, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "أُعيد طلب الإجازة للتعديل", "Leave request returned",
                    "أُعيد طلب إجازتك رقم {{Request.Number}} للتعديل. يرجى مراجعته.",
                    "Your leave request {{Request.Number}} was returned for changes."),
                new SeededRule("LEAVE_REQUEST:FinalApproved:Requester", WorkflowNotificationEvent.FinalApproved, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تمت الموافقة على طلب الإجازة", "Leave request approved",
                    "تمت الموافقة على طلب إجازتك رقم {{Request.Number}}.",
                    "Your leave request {{Request.Number}} has been approved."),
            },
            ["ATTENDANCE_CORRECTION"] = new[]
            {
                new SeededRule("ATTENDANCE_CORRECTION:Submitted:Requester", WorkflowNotificationEvent.Submitted, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم استلام طلب تصحيح الحضور", "Attendance correction received",
                    "تم استلام طلب تصحيح الحضور رقم {{Request.Number}} وهو قيد المراجعة.",
                    "Your attendance correction {{Request.Number}} was received and is under review."),
                new SeededRule("ATTENDANCE_CORRECTION:StepAssigned:CurrentApprover", WorkflowNotificationEvent.StepAssigned, null,
                    new[] { R(NotificationRecipientType.CurrentApprover) },
                    "طلب تصحيح حضور بانتظار موافقتك", "An attendance correction needs your approval",
                    "طلب تصحيح حضور رقم {{Request.Number}} من {{Employee.FullName}} بانتظار موافقتك.",
                    "Attendance correction {{Request.Number}} from {{Employee.FullName}} awaits your approval."),
                new SeededRule("ATTENDANCE_CORRECTION:Rejected:Requester", WorkflowNotificationEvent.Rejected, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم رفض طلب تصحيح الحضور", "Attendance correction rejected",
                    "نأسف لإبلاغك برفض طلب تصحيح الحضور رقم {{Request.Number}}.",
                    "Your attendance correction {{Request.Number}} was rejected."),
                new SeededRule("ATTENDANCE_CORRECTION:Returned:Requester", WorkflowNotificationEvent.Returned, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "أُعيد طلب تصحيح الحضور للتعديل", "Attendance correction returned",
                    "أُعيد طلب تصحيح الحضور رقم {{Request.Number}} للتعديل. يرجى مراجعته.",
                    "Your attendance correction {{Request.Number}} was returned for changes."),
                new SeededRule("ATTENDANCE_CORRECTION:FinalApproved:Requester", WorkflowNotificationEvent.FinalApproved, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تمت الموافقة على تصحيح الحضور", "Attendance correction approved",
                    "تمت الموافقة على طلب تصحيح الحضور رقم {{Request.Number}} وتم تطبيقه.",
                    "Your attendance correction {{Request.Number}} has been approved and applied."),
            },
        };

    public static IReadOnlyList<SeededRule> For(string requestCode)
        => Rules.TryGetValue(requestCode, out var r) ? r : Array.Empty<SeededRule>();
}
