using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Requests;

/// <summary>
/// Owns the provisioning lifecycle for built-in request types: create what is missing, upgrade what
/// is behind, and never touch what a tenant has made their own.
///
/// The upgrade contract, which is the part that matters:
///
///   • A request type a tenant authored (IsSystem = false) is never read or written here.
///   • A system request already at the shipped version is left completely alone.
///   • A system request behind the shipped version has its *required* effects reconciled — missing
///     ones are added, disabled ones re-enabled — and nothing else. Labels, icons, colours, optional
///     effects a tenant added, and optional effects a tenant disabled all survive.
///   • Nothing is ever deleted.
///
/// That asymmetry is deliberate. Required effects are what other modules depend on: if a tenant
/// could disable "deduct the leave balance" on the Leave Request, approved leave would stop
/// decrementing balances and payroll would quietly diverge. Everything else is theirs to change.
/// </summary>
public sealed class RequestProvisioningService : IRequestProvisioningService
{
    /// <summary>
    /// The version of the built-in request catalogue. Bump this when a system request's required
    /// effects change; tenants below it are upgraded on the next provisioning pass.
    ///
    /// v2: resignation/clearance/complaint gained load-bearing Task.Create (+ requester
    /// notification) effects, so existing tenants reconcile them on the next provision.
    /// </summary>
    public const int CurrentSeedVersion = 2;

    private readonly ApplicationDbContext _db;
    private readonly IRequestSeeder _seeder;
    private readonly IBackgroundExecutionContext _background;
    private readonly ILogger<RequestProvisioningService> _log;

    public RequestProvisioningService(
        ApplicationDbContext db,
        IRequestSeeder seeder,
        IBackgroundExecutionContext background,
        ILogger<RequestProvisioningService> log)
    { _db = db; _seeder = seeder; _background = background; _log = log; }

    public async Task<RequestProvisioningResult> ProvisionTenantAsync(Guid tenantId, Guid? actorUserId, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();

        // Everything below runs as this tenant. The ambient scope is what makes the global query
        // filter read the right rows and SaveChanges stamp the right TenantId — the seeder does not
        // take a tenant id and must not have one threaded through it.
        using var scope = _background.Begin(tenantId, actorUserId, email: null, correlationId: correlationId);

        _log.LogInformation(
            "Provisioning request types for tenant {TenantId} to seed version {SeedVersion} (correlation {CorrelationId}).",
            tenantId, CurrentSeedVersion, correlationId);

        // 1. Create anything missing. The seeder is idempotent per code, so this is a no-op for a
        //    tenant that already has the catalogue.
        var created = await _seeder.SeedSystemRequestsAsync(ct);

        // 2. Stamp and reconcile. Loaded after seeding so newly created types are included.
        var systemTypes = await _db.RequestTypes
            .Include(t => t.Effects)
            .Where(t => t.IsSystem)
            .ToListAsync(ct);

        var outcomes = new List<ProvisionedRequest>();
        var upgraded = 0;
        var current = 0;

        foreach (var type in systemTypes)
        {
            var from = type.SeedVersion;
            var changes = new List<string>();

            if (from >= CurrentSeedVersion)
            {
                outcomes.Add(new ProvisionedRequest(type.Code, ProvisionOutcome.AlreadyCurrent, from, from, changes));
                current++;
                continue;
            }

            changes.AddRange(ReconcileRequiredEffects(type));
            type.SeedVersion = CurrentSeedVersion;

            // from == 0 means the row had never been stamped: either just created above, or created
            // by an older build that predates SeedVersion. Both are "brought up to current".
            var outcome = from == 0 && changes.Count == 0 && created > 0
                ? ProvisionOutcome.Created
                : ProvisionOutcome.Upgraded;

            if (outcome == ProvisionOutcome.Upgraded) upgraded++;
            outcomes.Add(new ProvisionedRequest(type.Code, outcome, from, CurrentSeedVersion, changes));
        }

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Tenant {TenantId} provisioning complete: {Created} created, {Upgraded} upgraded, {Current} already current.",
            tenantId, created, upgraded, current);

        return new RequestProvisioningResult
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            Created = created,
            Upgraded = upgraded,
            AlreadyCurrent = current,
            Requests = outcomes,
        };
    }

    /// <summary>
    /// Brings a system request's required effects into line with what this version ships, additively.
    ///
    /// Only ever adds a missing required effect or re-enables a disabled one. An effect the tenant
    /// added is not in the required set and is not considered; an optional effect they disabled stays
    /// disabled. Sequence numbers on existing rows are left as the tenant ordered them.
    /// </summary>
    private List<string> ReconcileRequiredEffects(RequestType type)
    {
        var changes = new List<string>();
        if (!SystemRequestEffects.Required.TryGetValue(type.Code, out var required)) return changes;

        var existing = type.Effects.ToList();
        var nextSequence = existing.Count == 0 ? 0 : existing.Max(e => e.Sequence);

        foreach (var spec in required)
        {
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.EffectType, spec.EffectType, StringComparison.OrdinalIgnoreCase)
                && e.Trigger == spec.Trigger);

            if (match is null)
            {
                // Added through the DbSet, not through type.Effects. BaseEntity pre-assigns
                // Id = Guid.NewGuid(), and EF's graph tracking reads a pre-set key on an entity
                // reached via a navigation as "this row already exists" — it lands as Modified, the
                // UPDATE matches nothing, and TenantId is never stamped because stamping only runs
                // for Added. Adding to the set states the intent explicitly.
                _db.Set<RequestEffectDefinition>().Add(new RequestEffectDefinition
                {
                    RequestTypeId = type.Id,
                    EffectType = spec.EffectType,
                    Trigger = spec.Trigger,
                    EffectVersion = spec.EffectVersion,
                    Sequence = ++nextSequence,
                    IsEnabled = true,
                    IsRequired = true,
                    ExecutionMode = spec.ExecutionMode,
                    ConfigurationJson = EffectConfiguration.Serialize(spec.Inputs),
                });
                changes.Add($"added required effect {spec.EffectType}");
                continue;
            }

            // Repair, never overwrite. A required effect that was somehow disabled or demoted is
            // restored; its configuration is left alone, because a tenant may legitimately have
            // remapped an input to a renamed form field.
            if (!match.IsRequired) { match.IsRequired = true; changes.Add($"marked {spec.EffectType} required"); }
            if (!match.IsEnabled) { match.IsEnabled = true; changes.Add($"re-enabled required effect {spec.EffectType}"); }
        }

        return changes;
    }
}
