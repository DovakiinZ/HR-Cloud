# Configurable Attendance Permission Types — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let admins define per-tenant attendance-permission types (paid/unpaid, limits, exceed behavior, eligibility); enforce them at submit + approval; create a payroll deduction for unpaid permissions on a configurable wage basis.

**Architecture:** Reuse existing engines. Types = `MasterDataItem` (`ObjectType="AttendancePermissionType"`) with a typed `PermissionTypeRules` in `MetadataJson` (mirrors `LeaveType`/`LeaveRules`). Eligibility = `IScopeEngine` + `SelectionScope`. Limits = a generalized pure `AttendancePermissionCap` evaluator. Unpaid deduction = a born-Approved `PayrollTransaction` created in the `Attendance.CreatePermission` effect, mirroring `AttendanceCorrectionExecutor` (period guard + flag-if-finalized + idempotent). Wage basis via a config-driven resolver.

**Tech Stack:** .NET 8, EF Core (Npgsql, snake_case tables via explicit configs), xUnit + EF InMemory, MediatR, existing Completion-effect engine.

## Global Constraints

- No hard-coded limits: every limit is nullable; `null` ⇒ fall back to `AttendancePolicy`, else unlimited.
- No hard-coded wage basis: divisor days, daily payable hours, and eligible wage components are tenant config; 30 / 8 / basic-only are only defaults.
- Type limits are authoritative; `AttendancePolicy` cap (`PermissionMaxPerMonth`, `PermissionMaxMinutesPerMonth`, `PermissionCapMode`) is fallback only, and only for the monthly-minutes and monthly-requests dimensions.
- Preserve existing tenant customizations + approved permissions: seeding is idempotent and inserts only missing codes; never overwrite/hard-delete.
- Bilingual (Ar/En) user-facing text everywhere, matching existing style.
- Executors THROW to block (rolls back the completion transaction); `Skip` for benign no-ops; do NOT call `SaveChanges` in Add-only paths (the engine commits).
- Every increment: build clean, full test solution green (`dotnet test HR.sln`), then `git commit` + `git push`.
- Reference build/test invocation (absolute paths avoid the CWD doubling seen in this repo):
  - Build API: `dotnet build "D:/HR-Cloud-main/HR-Cloud-main/backend/src/HR.Api/HR.Api.csproj" -v q --nologo`
  - Run one project: `dotnet test "D:/HR-Cloud-main/HR-Cloud-main/backend/tests/HR.Domain.Finance.Tests/HR.Domain.Finance.Tests.csproj" --filter "FullyQualifiedName~<Name>" --nologo`
  - Migrations: `dotnet ef migrations add <Name> --project src/HR.Infrastructure --startup-project src/HR.Api --output-dir Migrations` (run from `backend`; local DB not required — use `--force` on remove).

---

## File Structure

**Layering note (resolved):** `HR.Domain` has NO project references and `SelectionScope` lives in `HR.Application`. Therefore: the `PermissionExceedBehavior` enum lives in **HR.Domain** (next to `AttendancePermissionCap`); `PermissionTypeRules` (carries `SelectionScope? Eligibility`) lives in **HR.Application**; the pure `AttendancePermissionCap.Evaluate` + `PermissionLimitSet` stay in **HR.Domain**; the rules→limits bridge `PermissionLimitResolver.Resolve(rules, policy)` lives in **HR.Application** (it references both). `HR.Modules.*` reference both layers, so executors/services see everything.

**Create:**
- `backend/src/HR.Application/Engines/Attendance/PermissionTypeRules.cs` — typed rules (`PermissionExceedBehavior` enum goes in `HR.Domain/Engines/Attendance/AttendancePermissionCap.cs` or a sibling Domain file).
- `backend/src/HR.Modules/Attendance/Services/AttendancePermissionTypeService.cs` — eligible-types + usage resolution.
- `backend/src/HR.Domain/Engines/Attendance/UnpaidPermissionDeduction.cs` — pure wage→amount math.
- `backend/src/HR.Modules/Attendance/Services/UnpaidPermissionWageResolver.cs` — monthly wage + divisor from config.
- `backend/tests/HR.Domain.Finance.Tests/PermissionTypeRulesTests.cs`
- `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionEligibilityTests.cs`
- `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionLimitsTests.cs`
- `backend/tests/HR.Domain.Finance.Tests/UnpaidPermissionDeductionTests.cs`
- `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionPayrollTests.cs`

**Modify:**
- `backend/src/HR.Domain/Engines/MasterData/MasterDataObjectType.cs` — add `AttendancePermissionType` const.
- `backend/src/HR.Infrastructure/Persistence/MasterDataDefaults.cs` — seed 5 types (+ `UNPAID_PERMISSION` deduction) with rules JSON.
- `backend/src/HR.Domain/Engines/Attendance/AttendancePermissionCap.cs` — generalize to a per-type limit set + `RequireOverride`.
- `backend/src/HR.Domain/Engines/Attendance/AttendancePolicy.cs` — add `UnpaidDivisorBasis`, `UnpaidDailyPayableHours`, `UnpaidWageComponentCodes`.
- `backend/src/HR.Modules/Attendance/Completion/AttendancePermissionCreateExecutor.cs` — type resolve, per-type limits, override reason, unpaid deduction.
- `backend/src/HR.Modules/Attendance/Controllers/AttendanceController.cs` — `eligible-types` + `validate` endpoints.
- `backend/src/HR.Infrastructure/Migrations/` — one migration for the 3 `AttendancePolicy` columns.
- `backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs` (+ provisioning) — seed the `ATTENDANCE_PERMISSION` system request type mapped to the effect.

---

## Task 1: Permission-type config model + seeding

**Files:**
- Create: `backend/src/HR.Application/Engines/Attendance/PermissionTypeRules.cs`
- Create: `backend/src/HR.Domain/Engines/Attendance/PermissionExceedBehavior.cs` (enum; or add it into the existing `AttendancePermissionCap.cs` — either is fine, Domain layer)
- Modify: `backend/src/HR.Domain/Engines/MasterData/MasterDataObjectType.cs`
- Modify: `backend/src/HR.Infrastructure/Persistence/MasterDataDefaults.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/PermissionTypeRulesTests.cs`

**Interfaces:**
- Produces (HR.Domain): `enum PermissionExceedBehavior { Block=0, Warn=1, RequireApprovalOverride=2 }`.
- Produces (HR.Application): `PermissionTypeRules` (props: `bool Paid`; `int? MaxMinutesPerRequest, MaxMinutesPerDay, MaxMinutesPerMonth, MaxRequestsPerDay, MaxRequestsPerMonth`; `PermissionExceedBehavior ExceedBehavior`; `SelectionScope? Eligibility`), and `static PermissionTypeRules Parse(string? metadataJson)` (tolerant: null/empty ⇒ defaults).
- Produces: `MasterDataObjectType.AttendancePermissionType = "AttendancePermissionType"`.

- [ ] **Step 1: Write the failing test** (`PermissionTypeRulesTests.cs`)

```csharp
using HR.Application.Engines.Attendance; // PermissionTypeRules
using HR.Domain.Engines.Attendance;      // PermissionExceedBehavior
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PermissionTypeRulesTests
{
    [Fact] // Missing/empty metadata → safe defaults (paid, unlimited, Block, no eligibility filter).
    public void Parse_null_returns_paid_unlimited_block()
    {
        var r = PermissionTypeRules.Parse(null);
        Assert.True(r.Paid);
        Assert.Null(r.MaxMinutesPerMonth);
        Assert.Null(r.MaxRequestsPerDay);
        Assert.Equal(PermissionExceedBehavior.Block, r.ExceedBehavior);
        Assert.Null(r.Eligibility);
    }

    [Fact] // Round-trips the config an admin would save.
    public void Parse_reads_limits_paid_and_behavior()
    {
        var json = "{\"paid\":false,\"maxMinutesPerDay\":120,\"maxRequestsPerMonth\":4,\"exceedBehavior\":2}";
        var r = PermissionTypeRules.Parse(json);
        Assert.False(r.Paid);
        Assert.Equal(120, r.MaxMinutesPerDay);
        Assert.Equal(4, r.MaxRequestsPerMonth);
        Assert.Equal(PermissionExceedBehavior.RequireApprovalOverride, r.ExceedBehavior);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test …HR.Domain.Finance.Tests… --filter "FullyQualifiedName~PermissionTypeRules"` → FAIL (type missing).

- [ ] **Step 3: Implement the enum (HR.Domain) then the rules (HR.Application)**

`backend/src/HR.Domain/Engines/Attendance/PermissionExceedBehavior.cs`:
```csharp
namespace HR.Domain.Engines.Attendance;

/// <summary>How a breached permission-type limit is handled.</summary>
public enum PermissionExceedBehavior { Block = 0, Warn = 1, RequireApprovalOverride = 2 }
```

`backend/src/HR.Application/Engines/Attendance/PermissionTypeRules.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Attendance;

namespace HR.Application.Engines.Attendance;

/// <summary>Typed view of an AttendancePermissionType MasterDataItem's MetadataJson (mirrors LeaveRules).
/// All limits nullable; null ⇒ fall back to AttendancePolicy, else unlimited. Eligibility null/Mode=All
/// ⇒ entire company.</summary>
public sealed class PermissionTypeRules
{
    public bool Paid { get; set; } = true;
    public int? MaxMinutesPerRequest { get; set; }
    public int? MaxMinutesPerDay { get; set; }
    public int? MaxMinutesPerMonth { get; set; }
    public int? MaxRequestsPerDay { get; set; }
    public int? MaxRequestsPerMonth { get; set; }
    public PermissionExceedBehavior ExceedBehavior { get; set; } = PermissionExceedBehavior.Block;
    public SelectionScope? Eligibility { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PermissionTypeRules Parse(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return new PermissionTypeRules();
        try { return JsonSerializer.Deserialize<PermissionTypeRules>(metadataJson, Opts) ?? new PermissionTypeRules(); }
        catch (JsonException) { return new PermissionTypeRules(); }
    }
}
```

- [ ] **Step 4: Run — PASS.**

- [ ] **Step 5: Add the object-type constant** — in `MasterDataObjectType.cs`, add `public const string AttendancePermissionType = "AttendancePermissionType";` next to `LeaveType`.

- [ ] **Step 6: Seed defaults** — in `MasterDataDefaults.Build()`, after the `LeaveType` block add (rules as MetadataJson; unlimited + eligible-all by omission; PERSONAL is unpaid):

```csharp
list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "GENERAL",         "General Permission",   "استئذان عام",   MetadataJson: "{\"paid\":true}"));
list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "EMERGENCY",       "Emergency Permission", "استئذان طارئ",  MetadataJson: "{\"paid\":true}"));
list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "PERSONAL",        "Personal Permission",  "استئذان شخصي",  MetadataJson: "{\"paid\":false}"));
list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "LATE_ARRIVAL",    "Late Arrival",         "تأخر صباحي",    MetadataJson: "{\"paid\":true}"));
list.Add(new MasterDataDefault(MasterDataObjectType.AttendancePermissionType, "EARLY_DEPARTURE", "Early Departure",      "انصراف مبكر",   MetadataJson: "{\"paid\":true}"));
```

And add one DeductionType for unpaid deductions (in the existing `Add(MasterDataObjectType.DeductionType, …)` list): `("UNPAID_PERMISSION", "Unpaid Permission Deduction", "خصم استئذان بدون أجر")`.

- [ ] **Step 7: Verify seeding is idempotent** — the existing `SeedDefaultMasterDataCommand` inserts only missing `(Tenant,ObjectType,Code)`; no code change needed. Confirm by reading the handler (`MasterDataCommands.cs`) — the unique index guarantees existing rows are untouched.

- [ ] **Step 8: Build + full suite** — `dotnet test HR.sln` green.

- [ ] **Step 9: Commit + push**

```bash
git add backend/src/HR.Domain/Engines/Attendance/PermissionTypeRules.cs backend/src/HR.Domain/Engines/MasterData/MasterDataObjectType.cs backend/src/HR.Infrastructure/Persistence/MasterDataDefaults.cs backend/tests/HR.Domain.Finance.Tests/PermissionTypeRulesTests.cs
git commit -m "feat(sp3): attendance-permission types — config model + seed 5 types + unpaid deduction type"
git push
```

---

## Task 2: Eligibility resolution + endpoint

**Files:**
- Create: `backend/src/HR.Modules/Attendance/Services/AttendancePermissionTypeService.cs`
- Modify: `backend/src/HR.Modules/Attendance/Controllers/AttendanceController.cs`
- Modify: `backend/src/HR.Modules/Attendance/DependencyInjection/DependencyInjection.cs` (register the service if not auto-scanned)
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionEligibilityTests.cs`

**Interfaces:**
- Consumes: `IScopeEngine.ResolveAsync(SelectionScope, ct)` → `ScopeResolution { IReadOnlyCollection<Guid> IncludedEmployeeIds, … }`; `PermissionTypeRules.Parse`; `MasterDataItem` (`ObjectType`, `Code`, `NameAr/En`, `IsActive`, `MetadataJson`).
- Produces:
  - `interface IAttendancePermissionTypeService { Task<IReadOnlyList<EligiblePermissionTypeDto>> GetEligibleTypesAsync(Guid employeeId, CancellationToken ct); Task<PermissionTypeContext?> ResolveForRequestAsync(Guid employeeId, string typeCodeOrId, CancellationToken ct); }`
  - `record EligiblePermissionTypeDto(Guid Id, string Code, string NameAr, string NameEn, bool Paid, PermissionExceedBehavior ExceedBehavior, PermissionUsageDto Usage, PermissionLimitsDto Limits)`
  - `record PermissionUsageDto(int UsedMinutesDay, int? RemainingMinutesDay, int UsedMinutesMonth, int? RemainingMinutesMonth, int UsedRequestsDay, int? RemainingRequestsDay, int UsedRequestsMonth, int? RemainingRequestsMonth)`
  - `record PermissionTypeContext(MasterDataItem Item, PermissionTypeRules Rules)`
  - `PermissionLimitsDto` = the 5 nullable limits (used by Task 3 too).

- [ ] **Step 1: Failing test** — five eligibility cases, using the in-memory `ApplicationDbContext` + `FakeUser` harness from `AttendancePermissionCreateExecutorTests.cs` (copy the `FakeUser`, `Ctx`, seed helpers). Seed employees in a dept/branch, seed types with different `Eligibility` metadata, assert which types come back.

```csharp
[Fact] public async Task Entire_company_type_is_eligible_for_everyone() { /* Eligibility null → included */ }
[Fact] public async Task Department_scoped_type_only_for_that_department() { /* Include Department criterion */ }
[Fact] public async Task Branch_scoped_type_only_for_that_branch() { }
[Fact] public async Task Specific_employees_type_only_for_listed_ids() { /* IncludeEmployeeIds */ }
[Fact] public async Task Excluded_employee_does_not_see_type() { /* All minus ExcludeEmployeeIds */ }
```

Use `SelectionScopeJson.Serialize(...)` (or hand-write JSON matching `SelectionScopeJson`) for the `MetadataJson` eligibility. For the test, construct `IScopeEngine` via its real implementation `ScopeEngine` with the same `ApplicationDbContext`, OR a small fake `IScopeEngine` that returns a fixed set — prefer the real `ScopeEngine` for a truthful test if its dependencies are constructible in-memory; otherwise a fake keyed on the scope.

- [ ] **Step 2: Run → FAIL** (service missing).

- [ ] **Step 3: Implement `AttendancePermissionTypeService`.** Load active `MasterDataItem` where `ObjectType==AttendancePermissionType`; parse rules; for each, if `Eligibility` null/`Mode=="All"` include, else `ResolveAsync` and check membership. Compute `PermissionUsageDto` by counting `AttendancePermission` rows for the employee in today / this-month windows (minutes = sum `ExcusedMinutes`, requests = row count); remaining = `limit - used` when the (type→policy fallback) limit is set, else `null`.

- [ ] **Step 4: Run → PASS.**

- [ ] **Step 5: Add endpoint** — in `AttendanceController`, `GET api/attendance/permissions/eligible-types` → `_types.GetEligibleTypesAsync(currentEmployeeId, ct)`. Resolve the caller's employee id the way other self-service endpoints do (find the pattern; e.g. `ICurrentUserService` → employee lookup). Permission: `Attendance.View` or self-service (match sibling self endpoints).

- [ ] **Step 6: Build + full suite green.**

- [ ] **Step 7: Commit + push** — `feat(sp3): eligible permission types resolution + endpoint (scope-engine eligibility)`.

---

## Task 3: Per-type limits + used/remaining + executor enforcement

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Attendance/AttendancePermissionCap.cs`
- Modify: `backend/src/HR.Modules/Attendance/Completion/AttendancePermissionCreateExecutor.cs`
- Modify: `backend/src/HR.Modules/Attendance/Controllers/AttendanceController.cs` (add `validate`)
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionLimitsTests.cs`
- Modify: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionCreateExecutorTests.cs` (adapt to new payload carrying `permissionTypeId`)

**Interfaces:**
- Produces (generalized evaluator, replacing the current 3-arg `Evaluate`):

```csharp
public enum AttendancePermissionCapOutcome { Allowed = 0, Warn = 1, Block = 2, RequireOverride = 3 }

public readonly record struct PermissionLimitSet(
    int? MaxMinutesPerRequest, int? MaxMinutesPerDay, int? MaxMinutesPerMonth,
    int? MaxRequestsPerDay, int? MaxRequestsPerMonth, PermissionExceedBehavior Behavior);

public readonly record struct PermissionUsageTally(
    int UsedMinutesDay, int UsedMinutesMonth, int UsedRequestsDay, int UsedRequestsMonth);

public readonly record struct AttendancePermissionCapDecision(
    AttendancePermissionCapOutcome Outcome, string? ReasonAr, string? ReasonEn)
{
    public bool IsBlocked => Outcome == AttendancePermissionCapOutcome.Block;
    public bool IsWarning => Outcome == AttendancePermissionCapOutcome.Warn;
    public bool RequiresOverride => Outcome == AttendancePermissionCapOutcome.RequireOverride;
    public static readonly AttendancePermissionCapDecision Allowed = new(AttendancePermissionCapOutcome.Allowed, null, null);
}

// HR.Domain — pure evaluator over a resolved limit set (no rules/Application types).
public static class AttendancePermissionCap
{
    // newRequestMinutes = in-shift ExcusedMinutes for THIS request.
    public static AttendancePermissionCapDecision Evaluate(
        PermissionLimitSet limits, PermissionUsageTally used, int newRequestMinutes);
}

// HR.Application — bridges Application `PermissionTypeRules` + Domain `AttendancePolicy` → Domain `PermissionLimitSet`.
public static class PermissionLimitResolver
{
    public static PermissionLimitSet Resolve(PermissionTypeRules rules, AttendancePolicy? policy);
}
```

`PermissionLimitSet`, `PermissionUsageTally`, `AttendancePermissionCapOutcome`, `AttendancePermissionCapDecision`, and `AttendancePermissionCap.Evaluate` live in **HR.Domain** (extend the existing `AttendancePermissionCap.cs`). `PermissionLimitResolver.Resolve` lives in **HR.Application** (it references `PermissionTypeRules`). `Resolve` mapping: `MaxMinutesPerMonth ??= policy?.PermissionMaxMinutesPerMonth`; `MaxRequestsPerMonth ??= policy?.PermissionMaxPerMonth`; behavior always from `rules.ExceedBehavior` (the type is authoritative for behavior). The other three limits have no policy fallback.

`Evaluate` logic: compute each `over*` boolean (`used + new > limit` when limit non-null; requests use `+1`; per-request compares `newRequestMinutes` alone). If none over → `Allowed`. Else outcome = behavior mapped (`Block→Block`, `Warn→Warn`, `RequireApprovalOverride→RequireOverride`); bilingual reason names the first breached limit.

- [ ] **Step 1: Failing tests** (`AttendancePermissionLimitsTests.cs`) — pure evaluator, no DB:

```csharp
// helpers: Limits(...) builds PermissionLimitSet; Used(...) builds tally.
[Fact] public void Unlimited_allows() { /* all null → Allowed even with huge usage */ }
[Fact] public void Per_request_minutes_block() { /* MaxMinutesPerRequest=60, new=90, Block → IsBlocked */ }
[Fact] public void Daily_minutes_cap_enforced() { /* used 100 + new 60 > 120 */ }
[Fact] public void Monthly_minutes_cap_enforced() { }
[Fact] public void Requests_per_day_cap_enforced() { /* usedRequestsDay 2, +1 > 2 */ }
[Fact] public void Requests_per_month_cap_enforced() { }
[Fact] public void Warn_behavior_yields_warn_not_block() { }
[Fact] public void Override_behavior_yields_require_override() { }
[Fact] public void Exactly_at_cap_is_allowed() { /* used 60 + new 60 == 120 → Allowed */ }
[Fact] public void Resolve_falls_back_to_policy_monthly_dims_only() { /* type null, policy set → used */ }
```

- [ ] **Step 2: Run → FAIL** (new API).

- [ ] **Step 3: Rewrite `AttendancePermissionCap`** to the interface above. Delete the old 3-arg `Evaluate`; update its only caller (the executor, Task-3 Step 6) and its existing tests (`AttendancePermissionCapEvaluatorTests.cs`) — port those to the new `Resolve`+`Evaluate` shape or fold into the new file (keep coverage of the AttendancePolicy fallback).

- [ ] **Step 4: Run → PASS.**

- [ ] **Step 5: Usage tally query** — add to `AttendancePermissionTypeService` (or a small helper reused by executor + endpoint): `Task<PermissionUsageTally> TallyAsync(Guid employeeId, DateTime date, ct)` counting rows/minutes for that day and that calendar month. Executor computes `newRequestMinutes` via existing `PermissionMath.WindowMinutesWithinShift`.

- [ ] **Step 6: Extend the executor** (`AttendancePermissionCreateExecutor`):
  - Read `permissionTypeId` (or `permissionTypeCode`) from payload; resolve the type + rules via `IAttendancePermissionTypeService.ResolveForRequestAsync`. If not resolvable → `throw NonRetryableEffectException` (bad config).
  - Keep the existing idempotency short-circuit (per `RequestInstanceId`) BEFORE limit evaluation, so re-runs are no-ops.
  - Build `PermissionLimitSet` via `Resolve(rules, policy)`, tally usage, evaluate.
    - `Block` → `throw NonRetryableEffectException(reason)`.
    - `RequireOverride` → require non-empty `overrideReason` in payload; if missing → `throw NonRetryableEffectException("… override reason required …")`. If present → proceed; stamp override into an `AttendanceAuditLog` (action `PermissionCapOverride`, DetailsAr/En = reason) and set `capOverride=true` in the result.
    - `Warn` → proceed; `capWarning=true`.
  - Persist the `AttendancePermission` (unchanged shape) with `Reason` = the request reason.

- [ ] **Step 7: Update executor tests** — `AttendancePermissionCreateExecutorTests.cs` payloads gain `permissionTypeId`; seed a matching `AttendancePermissionType` MasterDataItem in each test's DB. Keep create/idempotency/shift-clip tests; retarget the old policy-cap block/warn tests to type limits. Add: `Requires_override_reason_when_behavior_is_override` (missing reason → throws; present → writes + audit).

- [ ] **Step 8: Add `validate` endpoint** — `POST api/attendance/permissions/validate` body `{ permissionTypeId, date, fromTime, toTime }` → returns `{ durationMinutes, excusedMinutes, usage, decision (outcome+reason), overrideRequired }`. Reuses tally + `Resolve` + `Evaluate` + `WindowMinutesWithinShift`.

- [ ] **Step 9: Build + full suite green.**

- [ ] **Step 10: Commit + push** — `feat(sp3): per-type permission limits (day/month/requests) + validate endpoint + override-reason gate`.

---

## Task 4: Paid/unpaid → payroll deduction (configurable basis)

**Files:**
- Create: `backend/src/HR.Domain/Engines/Attendance/UnpaidPermissionDeduction.cs`
- Create: `backend/src/HR.Modules/Attendance/Services/UnpaidPermissionWageResolver.cs`
- Modify: `backend/src/HR.Domain/Engines/Attendance/AttendancePolicy.cs`
- Modify: `backend/src/HR.Modules/Attendance/Completion/AttendancePermissionCreateExecutor.cs`
- Create migration: `AttendancePermissionUnpaidBasis`
- Test: `backend/tests/HR.Domain.Finance.Tests/UnpaidPermissionDeductionTests.cs`, `AttendancePermissionPayrollTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class UnpaidPermissionDeduction
{
    // amount = round( (minutes/60) * (monthlyWage/divisorDays) / dailyPayableHours, 2 )
    public static decimal Amount(decimal monthlyWage, int minutes, int divisorDays, decimal dailyPayableHours);
}

public interface IUnpaidPermissionWageResolver
{
    // monthlyWage = BasicSalary + Σ eligible allowance components (config); divisorDays from UnpaidDivisorBasis.
    Task<(decimal MonthlyWage, int DivisorDays, decimal DailyPayableHours)> ResolveAsync(Guid employeeId, DateTime date, CancellationToken ct);
}
```
- Consumes: `IPayrollPeriodGuard.EnsurePeriodOpenForAsync(employeeId, date, ct)` (throws `PayrollPeriodClosedException`); `PayrollTransaction` (Kind=Deduction), `MasterDataItem` DeductionType `UNPAID_PERMISSION` → `TypeId`.
- `AttendancePolicy` new columns: `DayBasis UnpaidDivisorBasis = DayBasis.Fixed30`, `decimal UnpaidDailyPayableHours = 8m`, `string? UnpaidWageComponentCodes` (CSV; null ⇒ basic only).

- [ ] **Step 1: Failing tests — pure math** (`UnpaidPermissionDeductionTests.cs`):

```csharp
[Fact] // 4h unpaid, 12000/30/8 → hourly 50 → 200.
public void Four_hours_default_basis() =>
    Assert.Equal(200m, UnpaidPermissionDeduction.Amount(12000m, 240, 30, 8m));

[Fact] // Configurable basis changes the amount: divisor 26, 7 payable hours.
public void Non_default_basis_changes_amount() =>
    Assert.Equal(Math.Round(240/60m * (12000m/26m) / 7m, 2), UnpaidPermissionDeduction.Amount(12000m, 240, 26, 7m));

[Fact] public void Zero_minutes_is_zero() => Assert.Equal(0m, UnpaidPermissionDeduction.Amount(12000m, 0, 30, 8m));
```

- [ ] **Step 2: Run → FAIL.** **Step 3:** implement `UnpaidPermissionDeduction.Amount` (guard divisorDays/hours > 0). **Step 4:** PASS.

- [ ] **Step 5: Add `AttendancePolicy` columns** (defaults reproduce 30/8/basic). Add migration:

```bash
dotnet ef migrations add AttendancePermissionUnpaidBasis --project src/HR.Infrastructure --startup-project src/HR.Api --output-dir Migrations
```
Verify it only adds `UnpaidDivisorBasis` (int), `UnpaidDailyPayableHours` (numeric), `UnpaidWageComponentCodes` (text nullable) to `attendance_policies`.

- [ ] **Step 6: Implement `UnpaidPermissionWageResolver`** — load the active `AttendancePolicy` (basis/hours/component CSV) + the `Employee` (`BasicSalary`). If `UnpaidWageComponentCodes` set, sum matching allowance amounts (reuse the employee allowance source used by `PayrollFactProvider`; if not trivially available in-scope, sum from the employee's allowance collection). `DivisorDays`: `Fixed30→30`, `CalendarMonth→DateTime.DaysInMonth(date.Year,date.Month)`, `WorkingDays→` count of non-weekend days that month (reuse `AttendanceCalculationService.ParseWeekendDays` + shift weekend, else default 30). Default policy null ⇒ (BasicSalary, 30, 8).

- [ ] **Step 7: Failing payroll tests** (`AttendancePermissionPayrollTests.cs`) — executor-level, in-memory DB, seeding a `UNPAID_PERMISSION` DeductionType + `AttendancePermissionType` (paid & unpaid), `AttendancePolicy`, employee/shift:

```csharp
[Fact] public async Task Paid_permission_creates_no_deduction() { /* type paid → 0 PayrollTransactions */ }
[Fact] public async Task Unpaid_permission_creates_deduction_equal_to_hours() { /* 240 min in-shift, wage → amount; Kind=Deduction; ReferenceType="UnpaidPermission" */ }
[Fact] public async Task Unpaid_deduction_is_idempotent() { /* run twice (save between) → 1 deduction */ }
[Fact] public async Task Configurable_basis_is_honored() { /* set policy divisor 26/hours 7 → amount matches helper */ }
[Fact] public async Task Finalized_period_flags_instead_of_deducting() { /* fake IPayrollPeriodGuard throws → 0 deduction + a PayrollAdjustmentNeeded Notification */ }
```

Use a fake `IPayrollPeriodGuard`: default no-throw; the finalized test injects one that throws `PayrollPeriodClosedException`.

- [ ] **Step 8: Extend the executor** — inject `IUnpaidPermissionWageResolver` + `IPayrollPeriodGuard`. After writing the permission, if `rules.Paid == false` and `ExcusedMinutes > 0`:
  - Dedupe: skip if a `PayrollTransaction` with `ReferenceType=="UnpaidPermission"` && `ReferenceId==permission.Id` exists.
  - Guard: `try EnsurePeriodOpenForAsync(employee, date)`; on `PayrollPeriodClosedException` → add a `Notification` (Category `PayrollAdjustmentNeeded`, bilingual body naming the hours/amount, `Link="/payroll"`) and return Ok with `payrollAdjustmentFlagged=true` (no transaction).
  - Else compute `(monthlyWage, divisorDays, dailyHours)` → `amount = UnpaidPermissionDeduction.Amount(...)`; resolve `UNPAID_PERMISSION` DeductionType `TypeId` (query `MasterDataItem`); add a born-Approved `PayrollTransaction` (Kind=Deduction, SourceModule="AttendancePermission", ReferenceType="UnpaidPermission", ReferenceId=permission.Id, EffectiveDate=date, TransactionDate=date, TargetPeriodYear/Month from date, Status=Approved). Do NOT call SaveChanges (engine commits); tests save explicitly.

- [ ] **Step 9: Run → PASS. Build + full suite green.**

- [ ] **Step 10: Commit + push** — `feat(sp3): unpaid permission → payroll deduction on configurable basis (guard + flag + idempotent)`.

---

## Task 5: Wire the ATTENDANCE_PERMISSION system request type

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs` (+ any provisioning/seed-version file it uses)
- Test: `backend/tests/HR.Modules.Platform.Tests/…` (a seeding/provisioning test if the pattern exists)

**Interfaces:**
- Consumes: `EffectTypes.AttendanceCreatePermission`; the effect-config shape used by other system requests (study an existing entry, e.g. the leave or attendance-correction system request, in `SystemRequestEffects.cs`).

- [ ] **Step 1:** Read `SystemRequestEffects.cs` + `RequestProvisioningService.cs` to learn the exact shape for declaring a system request type, its form fields, and its effect mapping (trigger `FinalApproval`, input source mapping). Mirror the closest sibling (attendance correction).

- [ ] **Step 2:** Declare `ATTENDANCE_PERMISSION` with form fields `permissionType` (lookup `attendance-permission-types`), `date`, `fromTime`, `toTime`, `reason`, `overrideReason` (optional), mapped to `Attendance.CreatePermission` with per-input source mapping (FormField), under the `TIME_OFF` category. Bump the seed version if the framework uses one (follow the SP2 `SeedVersion N→N+1 reconcile` pattern noted in memory).

- [ ] **Step 3:** If a provisioning/seed test exists, extend it to assert the new type + effect mapping are declared; else add a focused test that the catalog/seed includes `ATTENDANCE_PERMISSION → Attendance.CreatePermission`.

- [ ] **Step 4:** Build + full suite green.

- [ ] **Step 5:** Commit + push — `feat(sp3): seed ATTENDANCE_PERMISSION system request type mapped to the create-permission effect`.

---

## Post-plan deployment note (not a code task)

After all increments: two migrations are pending on Azure (`AttendancePermissionsSchemaAndMonthlyCap` from `a690efa` + `AttendancePermissionUnpaidBasis` here). Applying to Azure Postgres + API redeploy + running master-data `seed-defaults` per tenant is a separate, user-initiated deploy step (see memory `sp3-attendance-permission-completed`).

## Self-Review

- **Spec coverage:** §3 config→Task 1; §4 eligibility→Task 2; §5 limits/used-remaining/override→Task 3; §6 paid/unpaid + §6.1 configurable basis→Task 4; §7 request-type wiring→Task 5; §9 tests distributed across Tasks 1–4. All covered.
- **Placeholder scan:** pure-logic units have full code; wiring tasks cite exact mirror sources (`AttendanceCorrectionExecutor`, `SystemRequestEffects`) rather than guessing 100+ lines — acceptable because the template is named and in-repo. Two spots require a pre-write check: `PermissionTypeRules` placement depends on whether `HR.Domain`→`HR.Application` reference exists (Task 1 Step 3 note); the eligible-employee-id resolution pattern in the controller (Task 2 Step 5) must match sibling self-service endpoints.
- **Type consistency:** `AttendancePermissionCapDecision`/`Outcome` reused Task 3→4; `PermissionLimitSet`/`PermissionUsageTally` consistent; `UnpaidPermissionDeduction.Amount` signature identical in Task 4 tests + impl; `ReferenceType="UnpaidPermission"` identical across dedupe + create + tests.
