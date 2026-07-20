using HR.Domain.Common;

namespace HR.Domain.Engines.Assets;

/// <summary>
/// One period during which an employee holds an asset.
///
/// An open custody is one with no <see cref="ReturnedAt"/>. "Currently assigned" is therefore a
/// query, not a flag on Asset — which keeps the history intact and means the uniqueness rule
/// ("an asset can only be in one person's hands at a time") is enforced against the same rows that
/// tell you who had it last.
/// </summary>
public class AssetCustody : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The request that produced this custody, when it came from one. Null for a custody
    /// assigned directly by HR.</summary>
    public Guid? RequestInstanceId { get; set; }

    public DateTime AssignedAt { get; set; }
    public DateTime? ExpectedReturnDate { get; set; }

    /// <summary>Null while the employee still holds the asset. Setting it closes the custody and
    /// frees the asset for reassignment.</summary>
    public DateTime? ReturnedAt { get; set; }

    public string? Notes { get; set; }

    public Asset Asset { get; set; } = null!;
}
