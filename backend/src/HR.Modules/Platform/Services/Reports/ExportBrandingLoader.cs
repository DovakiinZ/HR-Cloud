using HR.Application.Engines.Finance.Export;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Loads the tenant's CompanyProfile + logo bytes into a CompanyBranding for export headers.</summary>
public static class ExportBrandingLoader
{
    public static bool TryGetFileId(string? url, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        var idx = url.LastIndexOf('/');
        var tail = idx >= 0 ? url[(idx + 1)..] : url;
        return Guid.TryParse(tail, out id);
    }

    public static async Task<CompanyBranding?> LoadAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var c = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (c is null) return null;
        byte[]? logo = null;
        if (TryGetFileId(c.LogoUrl, out var fileId))
            logo = await db.Files.Where(f => f.Id == fileId).Select(f => f.Data).FirstOrDefaultAsync(ct);
        return new CompanyBranding(c.NameAr, c.NameEn, logo, c.CommercialRegistration, c.VatNumber, c.Phone, c.Email, c.Address);
    }
}
