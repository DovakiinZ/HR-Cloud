using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Requests;

/// <summary>
/// A request type the tenant can use. Unlike the old loose master-data approach, the
/// links to Form / Workflow / Print Template are REAL foreign keys — a request can only
/// exist (and be shown) if it is fully provisioned. "If visible, it is usable."
/// All classification is by object reference (governance), never free text.
/// </summary>
public class RequestType : TenantEntity
{
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    public RequestKind Kind { get; set; } = RequestKind.Dynamic;

    /// <summary>RequestCategory master-data item (object reference).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Required: the form an employee fills. A request without a form cannot exist.</summary>
    public Guid FormDefinitionId { get; set; }

    /// <summary>Optional: the separate workflow that drives approvals (referenced, not embedded).</summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>Optional: the official document template printed on approval.</summary>
    public Guid? PrintTemplateId { get; set; }

    /// <summary>For leave requests: which LeaveType master-data item this maps to.</summary>
    public Guid? LeaveTypeId { get; set; }

    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>
    /// The provisioning version this system request was last brought up to. Provisioning compares
    /// the shipped version against this and upgrades in place when it is behind, which is what lets
    /// a fix to a system request reach tenants that were provisioned before it. Zero for request
    /// types a tenant authored, which provisioning never touches.
    /// </summary>
    public int SeedVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public RequestImpactMapping? ImpactMapping { get; set; }
    public ICollection<RequestEffectDefinition> Effects { get; set; } = new List<RequestEffectDefinition>();
    public ICollection<RequestPermission> Permissions { get; set; } = new List<RequestPermission>();
}
