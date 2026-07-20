using System.Text.Json.Serialization;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Why a report was left alone. Serialized by name — this is read by administrators in a
/// remediation list, and "reason: 3" tells them nothing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackfillSkipReason
{
    /// <summary>System-managed (SYS_*). Ownerless by design and clone-only.</summary>
    SystemManaged = 1,
    /// <summary>No CreatedBy was recorded, so there is no evidence of who made it.</summary>
    NoCreatedBy = 2,
    /// <summary>CreatedBy names an account that does not exist in this tenant.</summary>
    CreatorNotFound = 3,
    /// <summary>CreatedBy matches more than one account; ambiguous, so no guess is made.</summary>
    CreatorAmbiguous = 4,
}

public sealed record BackfillAssignment(Guid ReportId, string Code, string Name, string CreatedBy, Guid OwnerId, string OwnerEmail);

public sealed record BackfillSkip(Guid ReportId, string Code, string Name, string? CreatedBy, BackfillSkipReason Reason);

public sealed record ReportOwnerBackfillResult
{
    public bool DryRun { get; init; }
    public int ScannedOwnerless { get; init; }

    /// <summary>Reports that received an owner (or would, on a dry run).</summary>
    public IReadOnlyList<BackfillAssignment> Assigned { get; init; } = Array.Empty<BackfillAssignment>();

    /// <summary>System reports, intentionally left ownerless.</summary>
    public IReadOnlyList<BackfillSkip> SystemManaged { get; init; } = Array.Empty<BackfillSkip>();

    /// <summary>Custom reports that could not be resolved. These need a tenant administrator to
    /// assign an owner deliberately — nothing here is guessed.</summary>
    public IReadOnlyList<BackfillSkip> Unresolved { get; init; } = Array.Empty<BackfillSkip>();
}

public interface IReportOwnerBackfill
{
    Task<ReportOwnerBackfillResult> RunAsync(bool dryRun, CancellationToken ct);
}

/// <summary>
/// Gives ownerless legacy reports an owner, using only evidence already in the row.
///
/// Reports created before ownership was recorded have OwnerId = null, and since CanEdit is
/// "owner OR a share granting edit", nobody can edit them. The fix is to restore the owner the
/// data already implies — not to invent one.
///
/// Deliberately conservative:
///   • never overwrites an existing OwnerId,
///   • never assigns an owner to a system report,
///   • never falls back to "the first admin" or to the caller — an unresolvable report is reported
///     for a human to decide, because a wrong owner silently hands someone else's report away,
///   • matches CreatedBy only against users in the same tenant, so a shared email address across
///     tenants cannot leak ownership sideways,
///   • refuses to guess when CreatedBy matches more than one account.
///
/// Idempotent: a second run finds nothing left to assign.
/// </summary>
public sealed class ReportOwnerBackfill : IReportOwnerBackfill
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;

    public ReportOwnerBackfill(ApplicationDbContext db, ICurrentUserService user)
    { _db = db; _user = user; }

    public async Task<ReportOwnerBackfillResult> RunAsync(bool dryRun, CancellationToken ct)
    {
        // The global query filter scopes this to the caller's tenant already; the explicit TenantId
        // predicate documents that and survives anyone disabling filters later.
        var tenantId = _user.TenantId;
        var ownerless = await _db.Set<ReportDefinition>()
            .Where(r => r.TenantId == tenantId && r.OwnerId == null)
            .OrderBy(r => r.Code)
            .ToListAsync(ct);

        // CreatedBy stores the creator's email (ApplicationDbContext stamps _currentUser.Email),
        // not a user id — so resolution is by email, within this tenant only.
        var emails = ownerless
            .Select(r => r.CreatedBy)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = emails.Count == 0
            ? new List<User>()
            : await _db.Set<User>()
                .Where(u => u.TenantId == tenantId && emails.Contains(u.Email))
                .ToListAsync(ct);

        // Group rather than ToDictionary: two accounts sharing an email would throw, and the right
        // response to ambiguity is to report it, not to crash the whole backfill.
        var byEmail = candidates
            .GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var assigned = new List<BackfillAssignment>();
        var system = new List<BackfillSkip>();
        var unresolved = new List<BackfillSkip>();

        foreach (var r in ownerless)
        {
            var name = r.NameAr ?? r.NameEn;

            if (ReportSystemPolicy.IsSystemManaged(r.Code))
            {
                system.Add(new BackfillSkip(r.Id, r.Code, name, r.CreatedBy, BackfillSkipReason.SystemManaged));
                continue;
            }

            var createdBy = r.CreatedBy?.Trim();
            if (string.IsNullOrWhiteSpace(createdBy))
            {
                unresolved.Add(new BackfillSkip(r.Id, r.Code, name, r.CreatedBy, BackfillSkipReason.NoCreatedBy));
                continue;
            }

            if (!byEmail.TryGetValue(createdBy, out var matches) || matches.Count == 0)
            {
                unresolved.Add(new BackfillSkip(r.Id, r.Code, name, r.CreatedBy, BackfillSkipReason.CreatorNotFound));
                continue;
            }

            if (matches.Count > 1)
            {
                unresolved.Add(new BackfillSkip(r.Id, r.Code, name, r.CreatedBy, BackfillSkipReason.CreatorAmbiguous));
                continue;
            }

            var owner = matches[0];
            assigned.Add(new BackfillAssignment(r.Id, r.Code, name, createdBy, owner.Id, owner.Email));
            if (!dryRun) r.OwnerId = owner.Id;
        }

        if (!dryRun && assigned.Count > 0) await _db.SaveChangesAsync(ct);

        return new ReportOwnerBackfillResult
        {
            DryRun = dryRun,
            ScannedOwnerless = ownerless.Count,
            Assigned = assigned,
            SystemManaged = system,
            Unresolved = unresolved,
        };
    }
}
