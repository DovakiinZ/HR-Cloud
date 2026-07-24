# Requests SP0 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the shared governance the 9 request-type sub-projects need — form-field classification with server-enforced locking of system-required fields, and manager request-context keys so notifications can target the requester's manager.

**Architecture:** Additive `FormField.MetadataJson` carries a classification (`SystemRequired`/`BusinessRequired`/`Optional`/`Custom`); a pure helper reads it with a total default. Field-edit MediatR handlers enforce the locks. The request-context resolver gains `managerUserId`/`managerEmail`, resolved in `CompletionEffectFactory` (which has DB access) and surfaced through `EffectResolutionContext` + `EffectValueResolver` + `RequestContextKeys.All`. Seeding stamps classification; provisioning backfills existing tenants only where absent.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql), MediatR, xUnit + FluentAssertions, in-memory `ApplicationDbContext`.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-25-requests-foundation-classification-notifications-design.md`.
- **Additive & backward-compatible.** Nullable column, defaulted classification (absent/invalid → `Optional`), new context keys. No existing effect/form/instance changes behavior.
- **Never overwrite tenant customizations.** Backfill classification ONLY where `MetadataJson` classification is absent.
- **System-required field lock:** cannot delete, disable, clear `IsRequired`, or change internal `Code`; **label/help-text edits (NameEn/NameAr/Placeholder) allowed**. A field mapped by a required effect input cannot be deleted regardless of classification.
- **recipientRole is OUT of scope** (deferred). Only `managerUserId`/`managerEmail` here.
- **Reuse, don't rebuild.** No new form/request/effect engine; extend existing services.
- Full backend suite stays green at every commit.
- **Commits:** one focused commit per task, clear message, trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. **Push to BOTH remotes (`origin`, `sanad`) before the next task.**
- Build: `dotnet build HR.sln -c Debug` · Platform tests: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj` (run from `backend/`). Migration is generated, **NOT applied** (deploy is a later user-gated step).

## File Structure

- `src/HR.Domain/Engines/Forms/FormField.cs` — add `MetadataJson`.
- `src/HR.Application/Engines/Forms/FormFieldClassification.cs` (NEW) — enum + pure helper.
- `src/HR.Infrastructure/Persistence/Configurations/**` (FormField config) — map `MetadataJson` jsonb.
- migration under `src/HR.Infrastructure/Migrations/`.
- `src/HR.Modules/Platform/Commands/Forms/UpdateFormFieldCommand.cs`, `DeleteFormFieldCommand.cs` — add lock guards in handlers.
- `src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs` + `RequestProvisioningService.cs` — stamp + backfill classification, SeedVersion 3.
- `src/HR.Application/Engines/Completion/EffectConfiguration.cs` — `RequestContextKeys` add `ManagerUserId`/`ManagerEmail`.
- `src/HR.Modules/Platform/Services/Completion/EffectValueResolver.cs` — resolve the two keys from ctx.
- `src/HR.Modules/Platform/Services/Completion/CompletionEffectFactory.cs` — resolve manager, populate `EffectResolutionContext`.
- `src/HR.Application/Engines/Completion/EffectConfiguration.cs`/`EffectValueResolver.cs` `EffectResolutionContext` — add `ManagerUserId`/`ManagerEmail`.
- Frontend: the form-editor field list component under `src/` — classification badge + disabled controls.
- Tests under `tests/HR.Modules.Platform.Tests/`.

---

## Task 1: FormField.MetadataJson + classification helper

**Files:**
- Modify: `src/HR.Domain/Engines/Forms/FormField.cs`
- Create: `src/HR.Application/Engines/Forms/FormFieldClassification.cs`
- Modify: the EF configuration mapping `FormField` (find it: `grep -rl "IEntityTypeConfiguration<FormField>" src/HR.Infrastructure` — likely `Persistence/Configurations/Engines/FormConfigurations.cs`)
- Migration: generated
- Test: `tests/HR.Modules.Platform.Tests/Forms/FormFieldClassificationTests.cs`

**Interfaces:**
- Produces: `FormField.MetadataJson` (string?); `enum FieldClassification { SystemRequired, BusinessRequired, Optional, Custom }`; `FormFieldClassification.Of(string? metadataJson) : FieldClassification`; `FormFieldClassification.IsLocked(FieldClassification) : bool`; `FormFieldClassification.With(FieldClassification) : string` (produces the MetadataJson).

- [ ] **Step 1: Write the failing test**

Create `tests/HR.Modules.Platform.Tests/Forms/FormFieldClassificationTests.cs`:
```csharp
using FluentAssertions;
using HR.Application.Engines.Forms;
using Xunit;

namespace HR.Modules.Platform.Tests.Forms;

public class FormFieldClassificationTests
{
    [Theory]
    [InlineData("{\"classification\":\"SystemRequired\"}", FieldClassification.SystemRequired)]
    [InlineData("{\"classification\":\"BusinessRequired\"}", FieldClassification.BusinessRequired)]
    [InlineData("{\"classification\":\"Optional\"}", FieldClassification.Optional)]
    [InlineData("{\"classification\":\"Custom\"}", FieldClassification.Custom)]
    public void Parses_declared_classification(string json, FieldClassification expected)
        => FormFieldClassification.Of(json).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"classification\":\"Bogus\"}")]
    public void Defaults_to_optional_when_absent_or_invalid(string? json)
        => FormFieldClassification.Of(json).Should().Be(FieldClassification.Optional);

    [Fact]
    public void Only_system_required_is_locked()
    {
        FormFieldClassification.IsLocked(FieldClassification.SystemRequired).Should().BeTrue();
        FormFieldClassification.IsLocked(FieldClassification.BusinessRequired).Should().BeFalse();
        FormFieldClassification.IsLocked(FieldClassification.Optional).Should().BeFalse();
        FormFieldClassification.IsLocked(FieldClassification.Custom).Should().BeFalse();
    }

    [Fact]
    public void With_round_trips_through_Of()
    {
        var json = FormFieldClassification.With(FieldClassification.SystemRequired);
        FormFieldClassification.Of(json).Should().Be(FieldClassification.SystemRequired);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~FormFieldClassificationTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Create the helper**

Create `src/HR.Application/Engines/Forms/FormFieldClassification.cs`:
```csharp
using System.Text.Json;

namespace HR.Application.Engines.Forms;

/// <summary>How a request form field is governed. Absent/unknown metadata is treated as Optional so
/// existing fields keep working unchanged.</summary>
public enum FieldClassification { SystemRequired, BusinessRequired, Optional, Custom }

/// <summary>Pure reader/writer for a FormField's classification, stored in FormField.MetadataJson as
/// {"classification":"...","isLocked":bool}. Total: any absent/invalid value → Optional.</summary>
public static class FormFieldClassification
{
    public static FieldClassification Of(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return FieldClassification.Optional;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("classification", out var c)
                && c.ValueKind == JsonValueKind.String
                && Enum.TryParse<FieldClassification>(c.GetString(), ignoreCase: true, out var parsed))
                return parsed;
        }
        catch (JsonException) { /* fall through to default */ }
        return FieldClassification.Optional;
    }

    public static bool IsLocked(FieldClassification c) => c == FieldClassification.SystemRequired;

    public static string With(FieldClassification c)
        => JsonSerializer.Serialize(new { classification = c.ToString(), isLocked = IsLocked(c) });
}
```

- [ ] **Step 4: Add the entity property**

In `src/HR.Domain/Engines/Forms/FormField.cs`, add after `Options`:
```csharp
    /// <summary>Governance metadata (JSONB): {"classification":"SystemRequired|BusinessRequired|Optional|Custom","isLocked":bool}. Null = Optional.</summary>
    public string? MetadataJson { get; set; }
```

- [ ] **Step 5: Map it as jsonb**

Find the FormField EF config (`grep -rl "IEntityTypeConfiguration<FormField>" src/HR.Infrastructure`). In that class's `Configure`, add:
```csharp
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
```
If FormField has no dedicated configuration class, add the property mapping wherever `FormField` is configured (e.g. inside the FormDefinition owned/related config), matching the existing jsonb mapping style used for `ValidationRules`/`Options`.

- [ ] **Step 6: Build + generate migration**

Run: `dotnet build HR.sln -c Debug` (expect success), then:
```bash
dotnet ef migrations add FormFieldClassificationMetadata --project src/HR.Infrastructure --startup-project src/HR.Api
```
Open the migration; confirm its `Up` ONLY adds the `MetadataJson` jsonb column to the form-fields table (`grep` the migration for `AddColumn` — exactly one, on the FormField table). If it contains unrelated model drift, STOP and report DONE_WITH_CONCERNS. **Do not apply.**

- [ ] **Step 7: Run helper tests + full suite**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: the 3 new test methods pass; full suite green.

- [ ] **Step 8: Commit + push**

```bash
git add src/HR.Domain src/HR.Application src/HR.Infrastructure tests
git commit -m "feat(requests): FormField classification metadata + helper

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 2: Server-enforced field locks

**Files:**
- Modify: `src/HR.Modules/Platform/Commands/Forms/DeleteFormFieldCommand.cs` (handler)
- Modify: `src/HR.Modules/Platform/Commands/Forms/UpdateFormFieldCommand.cs` (handler)
- Test: `tests/HR.Modules.Platform.Tests/Forms/FormFieldLockTests.cs`

**Interfaces:**
- Consumes: `FormFieldClassification.Of` (Task 1).
- Produces: guard behavior — see rules below.

Read both command files first to learn their handler shape (they are MediatR `IRequestHandler`s over `ApplicationDbContext`). The guards use the existing exception type the codebase throws for forbidden operations — find it (`grep -rn "class ForbiddenException" src`; `RequestEffectDefinitionService` throws it for locked required effects — reuse the same type).

- [ ] **Step 1: Write the failing tests**

Create `tests/HR.Modules.Platform.Tests/Forms/FormFieldLockTests.cs` with facts (seed an in-memory `ApplicationDbContext` with a `FormDefinition` + `FormField`s carrying `MetadataJson`, construct the real command handler, invoke it):
1. `Delete_system_required_field_is_blocked` — deleting a field whose `MetadataJson` classification is `SystemRequired` throws `ForbiddenException`; the field still exists.
2. `Delete_optional_field_is_allowed` — deleting an `Optional` field succeeds; the field is gone.
3. `Update_clearing_required_on_system_field_is_blocked` — `UpdateFormFieldCommand` that sets `IsRequired=false` on a `SystemRequired` field throws.
4. `Update_changing_code_of_non_custom_field_is_blocked` — changing `Code` on a `SystemRequired`/`Optional` field throws; changing `Code` on a `Custom` field is allowed.
5. `Update_label_and_placeholder_on_system_field_is_allowed` — changing NameEn/NameAr/Placeholder on a `SystemRequired` field succeeds and persists.
6. `Delete_field_mapped_by_a_required_effect_is_blocked` — a field (any classification) whose `Code` is referenced by an enabled `RequestEffectDefinition.IsRequired` effect's `ConfigurationJson` (source=FormField) cannot be deleted.

> Fact 6 requires cross-checking the request type's required effect definitions. Seed a `RequestType` (FormDefinitionId = the form), a required `RequestEffectDefinition` whose `ConfigurationJson` maps an input to `{"source":"FormField","key":"<fieldCode>"}`, then attempt to delete that field.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~FormFieldLockTests`
Expected: FAIL — no guards yet.

- [ ] **Step 3: Add the delete guard**

In `DeleteFormFieldCommand`'s handler, after loading the field (and before removing it), add:
```csharp
        var classification = FormFieldClassification.Of(field.MetadataJson);
        if (classification == FieldClassification.SystemRequired)
            throw new ForbiddenException("حقل نظامي مطلوب ولا يمكن حذفه", "System-required field cannot be deleted.");

        // A field feeding a required effect input cannot be deleted regardless of classification.
        if (await IsMappedByRequiredEffectAsync(field, ct))
            throw new ForbiddenException("هذا الحقل مرتبط بإجراء مطلوب ولا يمكن حذفه", "This field is used by a required effect and cannot be deleted.");
```
Add the private helper `IsMappedByRequiredEffectAsync` to the handler:
```csharp
    private async Task<bool> IsMappedByRequiredEffectAsync(FormField field, CancellationToken ct)
    {
        // Request types using this form, with their required, enabled effects.
        var configs = await _db.Set<RequestType>()
            .Where(t => t.FormDefinitionId == field.FormDefinitionId)
            .Join(_db.Set<RequestEffectDefinition>().Where(e => e.IsRequired && e.IsEnabled),
                  t => t.Id, e => e.RequestTypeId, (t, e) => e.ConfigurationJson)
            .ToListAsync(ct);
        foreach (var json in configs)
        {
            var cfg = EffectConfiguration.TryParse(json);
            if (cfg is null) continue;
            if (cfg.Inputs.Values.Any(m => m.Source == EffectValueSource.FormField
                    && string.Equals(m.Key, field.Code, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
```
(Match the handler's actual field-variable name and its `_db`/`ApplicationDbContext` member; add the needed `using`s: `HR.Application.Engines.Completion`, `HR.Domain.Engines.Requests`, `HR.Domain.Enums`, `HR.Application.Engines.Forms`, `Microsoft.EntityFrameworkCore`. Verify `ForbiddenException`'s real constructor signature — it may take a single message; if so, pass one bilingual string as the codebase does elsewhere.)

- [ ] **Step 4: Add the update guard**

In `UpdateFormFieldCommand`'s handler, after loading the existing field and before applying changes, add:
```csharp
        var classification = FormFieldClassification.Of(field.MetadataJson);

        if (classification == FieldClassification.SystemRequired)
        {
            if (!request.IsRequired)
                throw new ForbiddenException("لا يمكن تعطيل حقل نظامي مطلوب", "A system-required field cannot be made optional or disabled.");
        }

        if (classification != FieldClassification.Custom
            && !string.Equals(field.Code, request.Code, StringComparison.Ordinal))
            throw new ForbiddenException("لا يمكن تغيير المُعرّف الداخلي للحقل", "The internal field key cannot be changed.");
```
(Use the command's actual property names for the incoming values — `request.IsRequired`, `request.Code`, etc. Label/help-text/order changes proceed unchanged; do not block them.)

- [ ] **Step 5: Run the lock tests + full suite**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: the 6 lock facts pass; full suite green.

- [ ] **Step 6: Commit + push**

```bash
git add src/HR.Modules tests
git commit -m "feat(requests): lock system-required form fields against delete/disable/rename

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 3: Seed + backfill classification

**Files:**
- Modify: `src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs`
- Modify: `src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs`
- Test: `tests/HR.Modules.Platform.Tests/Requests/FieldClassificationSeedingTests.cs`

**Interfaces:**
- Consumes: `FormFieldClassification.With` (Task 1); `SystemRequestEffects.Required` (existing).
- Produces: seeded system fields carry classification; provisioning backfills existing tenants' fields where absent; `RequestProvisioningService.CurrentSeedVersion = 3`.

**Classification rule:** a system form field is `SystemRequired` iff its `Code` is referenced by any required effect input mapping (`SystemRequestEffects.Required[code]` → inputs with `source==FormField` → key) for a request type using that form; otherwise `Optional`. (BusinessRequired is applied per-request in later sub-projects, not here.)

- [ ] **Step 1: Write the failing tests**

Create `tests/HR.Modules.Platform.Tests/Requests/FieldClassificationSeedingTests.cs`:
1. `Backfill_stamps_system_required_on_effect_mapped_fields` — given a seeded system request whose form has a field mapped by a required effect and another that isn't, after provisioning the mapped field's `MetadataJson` classification is `SystemRequired` and the other is `Optional`.
2. `Backfill_does_not_overwrite_existing_classification` — a field that already has `MetadataJson` classification (e.g. a tenant set it to `Custom`) is left unchanged by provisioning.
3. `Backfill_is_idempotent` — running provisioning twice yields the same classifications and makes no further changes on the second pass.

Build on the existing provisioning test harness (`RequestProvisioningTests.cs`) — same in-memory context + seeding path.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~FieldClassificationSeedingTests`
Expected: FAIL.

- [ ] **Step 3: Bump the seed version**

In `RequestProvisioningService.cs`, change `CurrentSeedVersion` from `2` to `3`, updating the summary comment to note "v3: stamp form-field classification".

- [ ] **Step 4: Add the backfill pass**

In `RequestProvisioningService`, after `ReconcileRequiredEffects` runs for a system type (inside the per-type reconcile block), call a new `BackfillFieldClassifications(type, ct)` that:
```csharp
    private async Task<List<string>> BackfillFieldClassificationsAsync(RequestType type, CancellationToken ct)
    {
        var changes = new List<string>();
        var fields = await _db.Set<FormField>().Where(f => f.FormDefinitionId == type.FormDefinitionId).ToListAsync(ct);
        if (fields.Count == 0) return changes;

        // Codes referenced by this type's required effect inputs (source = FormField).
        var systemCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SystemRequestEffects.Required.TryGetValue(type.Code, out var specs))
            foreach (var spec in specs)
                foreach (var (_, mapping) in spec.Inputs)
                    if (mapping.Source == EffectValueSource.FormField && mapping.Key is { } k)
                        systemCodes.Add(k);

        foreach (var f in fields)
        {
            // Never overwrite an existing classification (tenant customization).
            if (!string.IsNullOrWhiteSpace(f.MetadataJson)
                && FormFieldClassification.Of(f.MetadataJson) is var _ && HasClassification(f.MetadataJson))
                continue;

            var target = systemCodes.Contains(f.Code) ? FieldClassification.SystemRequired : FieldClassification.Optional;
            f.MetadataJson = FormFieldClassification.With(target);
            changes.Add($"classified {f.Code} as {target}");
        }
        return changes;
    }

    private static bool HasClassification(string? json)
        => !string.IsNullOrWhiteSpace(json) && System.Text.Json.JsonDocument.Parse(json).RootElement
            .TryGetProperty("classification", out _);
```
Wire it into the reconcile loop and include its `changes` in the outcome, and ensure `_db.SaveChangesAsync` persists them (provisioning already saves at the end — confirm). Add `using HR.Application.Engines.Forms;`, `HR.Domain.Engines.Forms;`, `HR.Domain.Enums;`.
> `HasClassification` guards against overwriting; combine the two guards cleanly (the `Of(...) is var _` line is redundant — keep only `if (HasClassification(f.MetadataJson)) continue;`). Simplify to that single guard.

- [ ] **Step 5: Stamp classification in the seeder (new tenants)**

In `RequestSeeder.cs`, where system form fields are created, set `MetadataJson` at creation using the same rule (SystemRequired iff the field code is in the request's required-effect FormField inputs, else Optional). If the seeder builds forms generically, the simplest correct approach is to leave seeder fields with null `MetadataJson` and rely on the provisioning backfill (Step 4) which runs on every provision including immediately after seeding — verify the provisioning flow calls seeding then reconcile+backfill in one pass (it does: `SeedSystemRequestsAsync` then the reconcile loop). If so, **the seeder needs no change** and Step 5 is a no-op; state that in the report. Otherwise stamp at creation.

- [ ] **Step 6: Run tests + full suite**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: the 3 seeding facts pass; the existing `RequestProvisioningTests` (which asserts `SeedVersion == CurrentSeedVersion`) still pass; full suite green.

- [ ] **Step 7: Commit + push**

```bash
git add src/HR.Modules tests
git commit -m "feat(requests): seed + backfill form-field classification (SeedVersion 3)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 4: Manager request-context keys

**Files:**
- Modify: `src/HR.Application/Engines/Completion/EffectConfiguration.cs` (`RequestContextKeys`)
- Modify: `src/HR.Modules/Platform/Services/Completion/EffectValueResolver.cs` (`EffectResolutionContext` + `ResolveOne`)
- Modify: `src/HR.Modules/Platform/Services/Completion/CompletionEffectFactory.cs` (resolve manager, populate ctx)
- Test: `tests/HR.Modules.Platform.Tests/Completion/ManagerContextResolutionTests.cs`

**Interfaces:**
- Produces: `RequestContextKeys.ManagerUserId = "managerUserId"`, `RequestContextKeys.ManagerEmail = "managerEmail"` (both in `.All`); `EffectResolutionContext.ManagerUserId` (Guid?), `.ManagerEmail` (string?); resolver returns them for those keys.

- [ ] **Step 1: Write the failing tests**

Create `tests/HR.Modules.Platform.Tests/Completion/ManagerContextResolutionTests.cs`:
1. `Resolver_returns_manager_values_from_context` — an `EffectResolutionContext` with `ManagerUserId`/`ManagerEmail` set resolves a `RequestContext:managerEmail` mapping to that email and `managerUserId` to that id.
2. `Resolver_returns_null_when_manager_absent` — with `ManagerUserId`/`ManagerEmail` null, both keys resolve to null (no throw).
3. `Factory_populates_manager_from_employee_manager` — seed an employee with a `ManagerId` pointing to a manager employee (UserId + Email); after `CompletionEffectFactory.BuildAsync` for a request whose required effect maps `toEmail ← RequestContext:managerEmail`, the produced intent's payload `toEmail` equals the manager's email. (Reuse the Task-2 factory harness pattern from earlier sub-projects / `CompletionEffectFactoryDeferredTests`.)
4. `ManagerEmail_is_an_allowed_request_context_key` — `RequestContextKeys.All.Contains("managerEmail")` and `"managerUserId")` are true (so activation validation accepts the mapping).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ManagerContextResolutionTests`
Expected: FAIL.

- [ ] **Step 3: Add the keys**

In `EffectConfiguration.cs` `RequestContextKeys`, add after `DaysCount`:
```csharp
    public const string ManagerUserId = "managerUserId";
    public const string ManagerEmail = "managerEmail";
```
and add both to the `All` set (append to the HashSet initializer that currently lists EmployeeId..DaysCount).

- [ ] **Step 4: Extend the context + resolver**

In `EffectValueResolver.cs`, add to `EffectResolutionContext`:
```csharp
    public Guid? ManagerUserId { get; init; }
    public string? ManagerEmail { get; init; }
```
and add cases to `ResolveOne`'s `RequestContext` switch:
```csharp
            "manageruserid" => ctx.ManagerUserId,
            "manageremail" => ctx.ManagerEmail,
```

- [ ] **Step 5: Resolve the manager in the factory**

In `CompletionEffectFactory.BuildFromDefinitionsAsync`, before constructing the `EffectResolutionContext`, resolve the requester's manager:
```csharp
        Guid? managerUserId = null; string? managerEmail = null;
        var managerId = await _db.Employees.Where(e => e.Id == instance.EmployeeId)
            .Select(e => e.ManagerId).FirstOrDefaultAsync(ct);
        if (managerId is { } mid)
        {
            var mgr = await _db.Employees.Where(e => e.Id == mid)
                .Select(e => new { e.UserId, e.Email }).FirstOrDefaultAsync(ct);
            managerUserId = mgr?.UserId; managerEmail = mgr?.Email;
        }
```
and pass them into the `EffectResolutionContext` initializer (`ManagerUserId = managerUserId, ManagerEmail = managerEmail`).
> Verify `Employee` has `ManagerId` (Guid?), `UserId` (Guid?), and `Email` (string?) — adjust the projection to the real property names/types. If `UserId` is non-nullable or named differently, adapt.

- [ ] **Step 6: Run tests + full suite**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: the 4 manager facts pass; full suite green (existing RequestContext resolution unchanged).

- [ ] **Step 7: Commit + push**

```bash
git add src/HR.Application src/HR.Modules tests
git commit -m "feat(requests): manager request-context keys for notifications

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 5: UI — classification badge + locked controls

**Files:**
- Modify: the form-editor field-list component under `src/` (find it: `grep -rln "fields" src/**/settings/**/forms* ` or the form builder page; likely `src/components/**/form-*` or `src/app/(dashboard)/settings/**/forms/**`).
- Test: manual/visual (frontend); no unit test framework assumed for this component unless one exists.

**Interfaces:**
- Consumes: the field DTO must expose the classification. Confirm `FormFieldDto` includes `MetadataJson` or a derived `classification`; if not, add a read-only `classification` string to the DTO + its mapping (backend, small) so the UI can render it. (If added, that is a tiny additive backend change — include it here.)

- [ ] **Step 1: Expose classification on the field DTO (if absent)**

Check `FormFieldDto` (`grep -rn "class FormFieldDto\|record FormFieldDto" src/HR.Modules`). If it lacks classification, add a `Classification` string derived via `FormFieldClassification.Of(field.MetadataJson).ToString()` in the field→DTO mapping. Build; run the Platform suite (green).

- [ ] **Step 2: Render the badge + disable locked controls**

In the field-list component, for each field:
- Show a business badge when classification is `SystemRequired`: Arabic «حقل نظامي مطلوب», English "System-required".
- Disable the field's **delete** button and **required** toggle when `SystemRequired`.
- Make the internal key/`Code` input read-only for non-`Custom` fields.
- Keep label/help-text inputs editable.
Follow the component's existing styling (badges/buttons) — do not introduce a new design system.

- [ ] **Step 3: Verify build**

Run the frontend build/lint as the repo does (e.g. `npm run build` or `next build` under the frontend root) if quick; otherwise a type-check. Confirm no type errors on the changed component.

- [ ] **Step 4: Commit + push**

```bash
git add src
git commit -m "feat(requests): show classification badge, lock system-required field controls

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Self-Review

**Spec coverage:**
- A1 MetadataJson + helper → Task 1. ✓
- A2/A3 semantics + server guards (delete/disable/Code/required-mapping; label edits allowed) → Task 2. ✓
- A4 seed + non-destructive backfill + SeedVersion bump → Task 3. ✓
- A5 UI badge + disabled controls → Task 5. ✓
- B1/B2 manager context keys + resolver + factory → Task 4. ✓
- Compatibility (additive, defaults, no overwrite) → Tasks 1,3 (nullable column, default Optional, HasClassification guard). ✓
- Tests for every classification rule + manager resolution → Tasks 1,2,3,4. ✓

**Placeholder scan:** Task 5 is partly UI (no unit test harness assumed) — its verification is build + visual, explicitly stated, not a hidden TODO. Task 3 Step 5 and Task 5 Step 1 contain conditional "if absent" branches with the exact action for each branch — acceptable (the implementer verifies which branch applies and the action is specified for both).

**Type consistency:** `FieldClassification` enum + `FormFieldClassification.Of/IsLocked/With` used consistently (Tasks 1,2,3,5). `RequestContextKeys.ManagerUserId/ManagerEmail` and `EffectResolutionContext.ManagerUserId/ManagerEmail` consistent (Task 4). `ForbiddenException` reused from the codebase (Task 2, verify its constructor).

**Verification notes for implementers (flagged inline):** `ForbiddenException` constructor shape; the FormField EF config location; `Employee.ManagerId/UserId/Email` property names/nullability; whether provisioning runs seed→reconcile→backfill in one pass (making seeder Step 5 a no-op); whether `FormFieldDto` already carries classification.
