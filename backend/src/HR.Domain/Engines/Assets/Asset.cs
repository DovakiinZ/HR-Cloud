using HR.Domain.Common;

namespace HR.Domain.Engines.Assets;

/// <summary>
/// A company asset that can be handed to an employee as custody — a laptop, a phone, a vehicle.
///
/// Minimal on purpose: this exists to demonstrate that a new module can participate in the request
/// engine by adding an entity, an effect descriptor and an executor, with no change to the engine.
/// </summary>
public class Asset : TenantEntity
{
    /// <summary>Tenant-unique asset tag, e.g. "LAP-0042".</summary>
    public string Code { get; set; } = null!;

    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;

    /// <summary>MasterDataItem with ObjectType = AssetType.</summary>
    public Guid? AssetTypeId { get; set; }

    public string? SerialNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<AssetCustody> Custodies { get; set; } = new List<AssetCustody>();
}
