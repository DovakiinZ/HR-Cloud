using HR.Domain.Engines.Assets;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Requests;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Assets;

public interface IAssetLookupService
{
    /// <summary>
    /// Assets a custody request may actually ask for: active, and not currently in anyone's hands.
    /// </summary>
    Task<IReadOnlyList<AssignableAssetDto>> GetAssignableAsync(string? search, CancellationToken ct);

    /// <summary>Every asset with its current status, for an admin list.</summary>
    Task<IReadOnlyList<AssignableAssetDto>> GetAllAsync(string? search, CancellationToken ct);
}

/// <summary>
/// The asset feed behind the Custody Request's asset picker.
///
/// Availability is derived from custody rows rather than a flag on Asset: an asset is assigned iff
/// it has a custody with no ReturnedAt. That keeps the picker honest against the same rows the
/// executor's duplicate-assignment guard and the filtered unique index check, so the three can never
/// disagree — offering an asset that the effect would then refuse is the failure mode this avoids.
/// </summary>
public sealed class AssetLookupService : IAssetLookupService
{
    private readonly ApplicationDbContext _db;

    public AssetLookupService(ApplicationDbContext db) => _db = db;

    public Task<IReadOnlyList<AssignableAssetDto>> GetAssignableAsync(string? search, CancellationToken ct)
        => QueryAsync(search, assignableOnly: true, ct);

    public Task<IReadOnlyList<AssignableAssetDto>> GetAllAsync(string? search, CancellationToken ct)
        => QueryAsync(search, assignableOnly: false, ct);

    private async Task<IReadOnlyList<AssignableAssetDto>> QueryAsync(string? search, bool assignableOnly, CancellationToken ct)
    {
        // Tenant scoping is the global query filter's job on both sets; an open custody belonging to
        // another tenant is invisible here and so cannot mask an asset.
        var openCustodies = _db.Set<AssetCustody>().Where(c => c.ReturnedAt == null);

        var q = _db.Set<Asset>().AsNoTracking().Where(a => a.IsActive);

        if (assignableOnly)
            q = q.Where(a => !openCustodies.Any(c => c.AssetId == a.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(a =>
                EF.Functions.ILike(a.Code, term)
                || EF.Functions.ILike(a.NameAr, term)
                || EF.Functions.ILike(a.NameEn, term));
        }

        var rows = await q
            .OrderBy(a => a.Code)
            .Select(a => new
            {
                a.Id, a.Code, a.NameAr, a.NameEn, a.AssetTypeId,
                IsAssigned = openCustodies.Any(c => c.AssetId == a.Id),
            })
            .ToListAsync(ct);

        // Category names come from master data; resolved in one round trip rather than per row.
        var typeIds = rows.Where(r => r.AssetTypeId is not null).Select(r => r.AssetTypeId!.Value).Distinct().ToList();
        var categories = typeIds.Count == 0
            ? new Dictionary<Guid, (string Ar, string En)>()
            : await _db.MasterDataItems.AsNoTracking()
                .Where(m => typeIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => (m.NameAr, m.NameEn), ct);

        return rows.Select(r =>
        {
            (string Ar, string En)? cat = r.AssetTypeId is { } id && categories.TryGetValue(id, out var c) ? c : null;
            return new AssignableAssetDto
            {
                Id = r.Id,
                Code = r.Code,
                NameAr = r.NameAr,
                NameEn = r.NameEn,
                CategoryId = r.AssetTypeId,
                CategoryNameAr = cat?.Ar,
                CategoryNameEn = cat?.En,
                Status = r.IsAssigned ? "Assigned" : "Available",
            };
        }).ToList();
    }
}
