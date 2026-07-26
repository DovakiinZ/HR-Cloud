using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Notifications;

/// <summary>An admin/seed-configured rule that fires notifications on a request-workflow event.
/// Recipients are stored as a validated JSON envelope (see RecipientSpecParser). System-seeded rows
/// carry a stable SystemKey and are never overwritten once a tenant customizes them.</summary>
public class WorkflowNotificationRule : TenantEntity
{
    /// <summary>Request type code this applies to, or null = all types.</summary>
    public string? RequestTypeCode { get; set; }

    public WorkflowNotificationEvent Event { get; set; }

    /// <summary>Approval step this applies to, or null = any step.</summary>
    public int? StepOrder { get; set; }

    /// <summary>Validated recipient envelope: {"v":1,"recipients":[{"type":"...","refId":"..."}]}.</summary>
    public string RecipientsJson { get; set; } = """{"v":1,"recipients":[]}""";

    public string SubjectAr { get; set; } = "";
    public string SubjectEn { get; set; } = "";
    public string BodyAr { get; set; } = "";
    public string BodyEn { get; set; } = "";

    public bool ChannelBell { get; set; } = true;
    public bool ChannelEmail { get; set; } = true;
    public bool IsActive { get; set; } = true;

    /// <summary>True for product-seeded rows; tenant-authored rules are false.</summary>
    public bool IsSystemOwned { get; set; }

    /// <summary>Stable seed identity (e.g. "LEAVE_REQUEST:Submitted:Requester"). Unique per tenant when set.</summary>
    public string? SystemKey { get; set; }

    /// <summary>Set true when a tenant edits a system rule — provisioning then never overwrites it.</summary>
    public bool IsCustomized { get; set; }
}
