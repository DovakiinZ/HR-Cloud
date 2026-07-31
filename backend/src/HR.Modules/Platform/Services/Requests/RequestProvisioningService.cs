using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Forms;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Notifications;
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
    /// v3: stamp form-field classification (SystemRequired / Optional) on first provision;
    /// backfill existing tenants' fields where classification is absent.
    /// v4: seed default workflow notification rules for LEAVE_REQUEST (5 rules: Submitted,
    /// StepAssigned, Rejected, Returned, FinalApproved) — non-destructive; never overwrites
    /// a tenant-customized or tenant-authored rule.
    /// v5: reconcile missing shipped system form fields by Code (additive, never removes), and
    /// refresh the system effect's ConfigurationJson for still-required effects on system-owned,
    /// un-customized ATTENDANCE_CORRECTION forms.
    /// v6: seed ATTENDANCE_PERMISSION system request type with a 6-field form (permissionType,
    /// date, fromTime, toTime, reason, overrideReason) mapped to Attendance.CreatePermission;
    /// existing tenants that already had the type reconcile the required effect on upgrade.
    /// </summary>
    public const int CurrentSeedVersion = 6;

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
            changes.AddRange(await BackfillFieldClassificationsAsync(type, ct));
            changes.AddRange(ReconcileWorkflowNotificationRules(type));
            changes.AddRange(await ReconcileSystemFormFieldsAsync(type, ct));
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

    /// <summary>
    /// Stamps every FormField on this request type's form with a classification, additively.
    ///
    /// A field whose MetadataJson already contains a "classification" property is left completely
    /// alone — the tenant may have set it to Custom (or explicitly to Optional / SystemRequired) and
    /// that choice survives any number of provisioning passes.
    ///
    /// Classification rule: a field is SystemRequired iff its Code is referenced by at least one
    /// required-effect input whose Source is FormField for this request type; otherwise Optional.
    /// </summary>
    private async Task<List<string>> BackfillFieldClassificationsAsync(RequestType type, CancellationToken ct)
    {
        var changes = new List<string>();
        var formId = type.FormDefinitionId;
        if (formId == Guid.Empty) return changes;

        var fields = await _db.Set<FormField>()
            .Where(f => f.FormDefinitionId == formId)
            .ToListAsync(ct);
        if (fields.Count == 0) return changes;

        // Collect all FormField keys referenced by this type's required effect inputs.
        var systemCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SystemRequestEffects.Required.TryGetValue(type.Code, out var specs))
        {
            foreach (var spec in specs)
                foreach (var (_, mapping) in spec.Inputs)
                    if (mapping.Source == EffectValueSource.FormField && mapping.Key is { Length: > 0 } k)
                        systemCodes.Add(k);
        }

        foreach (var field in fields)
        {
            // Never overwrite an existing explicit classification (even if it is Optional).
            if (HasClassification(field.MetadataJson)) continue;

            var target = systemCodes.Contains(field.Code)
                ? FieldClassification.SystemRequired
                : FieldClassification.Optional;
            field.MetadataJson = FormFieldClassification.With(target);
            changes.Add($"classified {field.Code} as {target}");
        }

        return changes;
    }

    /// <summary>Seed the product-default workflow notification rules for a system request type.
    /// Non-destructive: inserts only rules whose SystemKey is absent, upgrades an untouched
    /// system-owned rule's content, and never modifies a tenant-authored or customized rule.</summary>
    internal List<string> ReconcileWorkflowNotificationRules(RequestType type)
    {
        var changes = new List<string>();
        var seeded = SystemWorkflowNotificationRules.For(type.Code);
        if (seeded.Count == 0) return changes;

        var existing = _db.WorkflowNotificationRules
            .Where(r => r.TenantId == type.TenantId && r.SystemKey != null)
            .ToDictionary(r => r.SystemKey!, StringComparer.Ordinal);

        foreach (var s in seeded)
        {
            if (existing.TryGetValue(s.SystemKey, out var row))
            {
                if (row.IsCustomized) continue;           // tenant edited a system rule → never touch
                // Safe in-place upgrade of an untouched system rule.
                row.Event = s.Event; row.StepOrder = s.StepOrder;
                row.RecipientsJson = RecipientSpecParser.Serialize(s.Recipients);
                row.SubjectAr = s.SubjectAr; row.SubjectEn = s.SubjectEn;
                row.BodyAr = s.BodyAr; row.BodyEn = s.BodyEn;
                continue;
            }
            _db.WorkflowNotificationRules.Add(new WorkflowNotificationRule
            {
                Id = Guid.NewGuid(), TenantId = type.TenantId, RequestTypeCode = type.Code,
                Event = s.Event, StepOrder = s.StepOrder,
                RecipientsJson = RecipientSpecParser.Serialize(s.Recipients),
                SubjectAr = s.SubjectAr, SubjectEn = s.SubjectEn, BodyAr = s.BodyAr, BodyEn = s.BodyEn,
                ChannelBell = true, ChannelEmail = true, IsActive = true,
                IsSystemOwned = true, SystemKey = s.SystemKey, IsCustomized = false,
            });
            changes.Add($"+notif:{s.SystemKey}");
        }
        return changes;
    }

    /// <summary>Add shipped system form fields that are missing from a system request's form (by Code),
    /// and refresh the system effect's ConfigurationJson for still-required effects to the shipped
    /// mapping. Never removes or reorders tenant fields; never touches a tenant-authored
    /// (IsSystem == false) type or a non-required effect.</summary>
    private async Task<List<string>> ReconcileSystemFormFieldsAsync(RequestType type, CancellationToken ct)
    {
        var changes = new List<string>();
        if (!type.IsSystem || type.FormDefinitionId == Guid.Empty) return changes;

        // Shipped fields for this request code, from the seeder's form spec.
        var shipped = _seeder.SystemFormFields(type.Code);
        if (shipped.Count == 0) return changes;

        var existing = await _db.Set<FormField>()
            .Where(f => f.FormDefinitionId == type.FormDefinitionId)
            .ToListAsync(ct);
        var byCode = existing.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        var maxSort = existing.Count == 0 ? 0 : existing.Max(f => f.SortOrder);

        foreach (var spec in shipped)
        {
            if (byCode.ContainsKey(spec.Code)) continue;      // present (tenant may have edited it) → leave
            _db.Set<FormField>().Add(new FormField
            {
                FormDefinitionId = type.FormDefinitionId,
                Code = spec.Code,
                NameAr = spec.NameAr,
                NameEn = spec.NameEn,
                FieldType = spec.FieldType,
                IsRequired = spec.IsRequired,
                Placeholder = spec.Placeholder,
                Options = spec.Options,
                SortOrder = ++maxSort,
                MetadataJson = FormFieldClassification.With(FieldClassification.Optional),
            });
            changes.Add($"+field:{spec.Code}");
        }

        // Merge-missing-keys into the ConfigurationJson for still-required effects.
        //
        // Deliberately NOT a wholesale replace: a tenant may have legitimately remapped an input
        // (e.g. renamed the "reason" form field to "justification" and updated the effect's config
        // to point at "justification"). Overwriting their mapping would silently break their effect.
        //
        // Strategy: parse the current config, add only the keys that are absent from it, and write
        // back only when at least one key was actually added. Keys already present — including any
        // tenant-remapped ones — are left completely untouched.
        if (SystemRequestEffects.Required.TryGetValue(type.Code, out var specs))
        {
            foreach (var s in specs)
            {
                var eff = type.Effects.FirstOrDefault(e =>
                    string.Equals(e.EffectType, s.EffectType, StringComparison.OrdinalIgnoreCase)
                    && e.Trigger == s.Trigger
                    && e.IsRequired);
                if (eff is null) continue;

                var current = EffectConfiguration.TryParse(eff.ConfigurationJson);
                if (current is null) continue;   // malformed JSON — do not corrupt it further

                var currentInputs = current.Inputs;
                var added = false;
                foreach (var (key, mapping) in s.Inputs)
                {
                    if (currentInputs.ContainsKey(key)) continue;   // present (possibly tenant-remapped) → leave
                    currentInputs[key] = mapping;
                    added = true;
                }

                if (added)
                {
                    eff.ConfigurationJson = EffectConfiguration.Serialize(currentInputs);
                    changes.Add($"~cfg:{s.EffectType}");
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// Returns true iff the JSON has an explicit "classification" property — even if its value is
    /// "Optional". This is deliberately different from <see cref="FormFieldClassification.Of"/>,
    /// which defaults absent/invalid JSON to Optional; using Of() as a guard would incorrectly treat
    /// unclassified fields as already classified and skip them.
    /// </summary>
    private static bool HasClassification(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("classification", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
