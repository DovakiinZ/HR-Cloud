# Configurable Attendance Permission Types — Design

**Date:** 2026-08-01
**Status:** Approved (design)
**Builds on:** SP3 attendance-permission (`AttendancePermission` entity, `Attendance.CreatePermission` effect, `AttendancePermissionCap`, `PermissionMath`), commit `a690efa`.

## 1. Goal

Turn the single `ATTENDANCE_PERMISSION` (استئذان) request into a configurable-by-admin system where each tenant defines **permission types** (General, Emergency, Personal, Late Arrival, Early Departure, …) with their own paid/unpaid rule, limits, exceed behavior, and eligibility. When an employee submits, they see only the types they are eligible for, with live duration + used/remaining, and configured limits are enforced. Paid permissions never touch payroll; unpaid permissions create a payroll deduction equal to the approved working hours.

**Non-negotiables:** no hard-coded limits or wage bases; reuse the existing entities/engines; preserve existing tenant customizations and already-approved permissions.

## 2. Key decisions (confirmed)

1. **Company = tenant.** "Entire company" = scope `Mode=All`. No separate Company/legal-entity dimension; Branch / Department / specific-employees / excluded-employees cover the rest.
2. **Config storage = `MasterDataItem`** (`ObjectType="AttendancePermissionType"`) with a typed rules object in `MetadataJson`, mirroring `LeaveType`/`LeaveRules`. Reuses `MasterDataController` CRUD, per-tenant seeding, and `/lookups`.
3. **Unpaid deduction created at approval** inside the `Attendance.CreatePermission` effect, mirroring `AttendanceCorrectionExecutor` (period guard + flag-if-finalized + idempotent reference).
4. **Backend-first**; admin settings UI and employee submit surfacing are a later pass.
5. **Type limits are authoritative;** the tenant `AttendancePolicy` cap (shipped in `a690efa`) is used only as a fallback when a type leaves a limit `null`.
6. **`RequireApprovalOverride`:** submission is allowed and the exceeded limit is flagged to the approver; finalizing requires an explicit override with a **mandatory reason**, which is recorded/audited.
7. **Unpaid wage basis is configurable per tenant** (salary divisor, daily payable hours, eligible wage components); 30 days / 8 hours / basic-only are only defaults.

## 3. Config model

`MasterDataItem` with `ObjectType="AttendancePermissionType"`. `IsActive` = active/inactive; `NameAr/NameEn` = names; `Code` stable. `MetadataJson` deserializes to:

```csharp
public sealed class PermissionTypeRules
{
    public bool Paid { get; set; } = true;

    // Limits — all nullable; null = "not set → fall back to AttendancePolicy, else unlimited".
    public int? MaxMinutesPerRequest { get; set; }
    public int? MaxMinutesPerDay { get; set; }
    public int? MaxMinutesPerMonth { get; set; }
    public int? MaxRequestsPerDay { get; set; }
    public int? MaxRequestsPerMonth { get; set; }

    public PermissionExceedBehavior ExceedBehavior { get; set; } = PermissionExceedBehavior.Block;

    // Eligibility — reuse the payroll SelectionScope. Null or Mode=All ⇒ entire company.
    public SelectionScope? Eligibility { get; set; }
}

public enum PermissionExceedBehavior { Block = 0, Warn = 1, RequireApprovalOverride = 2 }
```

`PermissionExceedBehavior` supersedes the 2-value `PermissionCapMode` at the type level; the policy-level fallback keeps using its own `PermissionCapMode` (Block/Warn).

**Seeding (idempotent, per tenant, only inserts missing codes):** add to `MasterDataDefaults`:

| Code | NameEn / NameAr | Paid |
|------|-----------------|------|
| `GENERAL` | General Permission / استئذان عام | paid |
| `EMERGENCY` | Emergency Permission / استئذان طارئ | paid |
| `PERSONAL` | Personal Permission / استئذان شخصي | unpaid |
| `LATE_ARRIVAL` | Late Arrival / تأخر صباحي | paid |
| `EARLY_DEPARTURE` | Early Departure / انصراف مبكر | paid |

Defaults: all limits `null` (unlimited), `Eligibility = All`, `IsSystemDefault=true`. Also seed a `UNPAID_PERMISSION` **DeductionType** master-data item (the `TypeId` for unpaid deductions). Seeding never overwrites existing rows → tenant customizations preserved.

## 4. Eligibility resolution

`IAttendancePermissionTypeService.GetEligibleTypesAsync(employeeId)`:
- Load active `AttendancePermissionType` items (+ parsed rules).
- For each: `Eligibility` null or `Mode=All` ⇒ eligible; else `IScopeEngine.ResolveAsync(scope)` and keep if `employeeId ∈ IncludedEmployeeIds`.
- Returns eligible types + rules + a `PermissionUsageDto` (used/remaining today and this month; `null` remaining = unlimited).

Endpoint `GET /api/attendance/permissions/eligible-types` (self-service; the employee's own id). Maps: entire company=`Mode=All`; branch/department=`Include` criteria; specific employees=`IncludeEmployeeIds`; excluded=`ExcludeEmployeeIds`.

## 5. Limits + used/remaining

Generalize `AttendancePermissionCap` into an evaluator over a **limit set** (the 5 limits + `ExceedBehavior`) plus current tallies:

```
inputs:  limits (from type, each null → AttendancePolicy fallback → unlimited),
         usedMinutesDay, usedMinutesMonth, usedRequestsDay, usedRequestsMonth,
         thisRequestMinutes (in-shift ExcusedMinutes), thisRequestDurationMinutes (end−start)
checks:  MaxMinutesPerRequest vs thisRequestMinutes
         MaxMinutesPerDay    vs usedMinutesDay + thisRequestMinutes
         MaxMinutesPerMonth  vs usedMinutesMonth + thisRequestMinutes
         MaxRequestsPerDay   vs usedRequestsDay + 1
         MaxRequestsPerMonth vs usedRequestsMonth + 1
outcome: Allowed | Warn | Block | RequireOverride   (+ bilingual reason naming the breached limit)
```

- **Duration** = `end − start`. **Tally value** = in-shift `ExcusedMinutes` (`PermissionMath.WindowMinutesWithinShift`) — the same value used for pay/attendance.
- **Requests** counted from `AttendancePermission` rows for the employee in the day/month window.
- Outcome mapping when any limit is breached: the type's `ExceedBehavior` decides `Warn` vs `Block` vs `RequireOverride` (all breached limits reported).

Wired into:
- `POST /api/attendance/permissions/validate` → `{ durationMinutes, excusedMinutes, usage, decision, overrideRequired, reason }` for live feedback.
- The `Attendance.CreatePermission` **executor** (approval-time gate):
  - `Block` → `throw NonRetryableEffectException(reason)`.
  - `RequireOverride` → require a non-empty `overrideReason` in the effect payload; if missing → throw (cannot finalize an over-limit permission without the mandatory override reason). If present → allow, stamp the override (reason + actor) into an `AttendanceAuditLog` and a notification, and flag `capOverride=true` in the result.
  - `Warn` → allow, flag in summary/after-state.

## 6. Paid vs unpaid → payroll

In the executor, after writing the `AttendancePermission`, resolve the selected type (payload `permissionTypeId`/`code`) and read `Paid`:

- **Paid:** nothing further — the attendance excuse already prevents a shortage deduction, and recalculation keeps honoring the permission window.
- **Unpaid:** create a born-Approved `PayrollTransaction` (Kind=Deduction):
  - `hours = ExcusedMinutes / 60`.
  - `amount = round( hours × (monthlyWage / divisorDays) / dailyPayableHours, 2 )` via a pure, tested `UnpaidPermissionDeduction.Amount(...)`. Defaults reproduce `monthlyWage/30/8`.
  - `SourceModule="AttendancePermission"`, `ReferenceType="UnpaidPermission"`, `ReferenceId=permission.Id`, `TypeId=` seeded `UNPAID_PERMISSION`, `EffectiveDate=permission.Date`, target period derived from it.
  - **Dedupe:** skip if a txn with `ReferenceType="UnpaidPermission"` + `ReferenceId=permission.Id` already exists (the permission itself is already idempotent per request instance).
  - **Finalized period:** `IPayrollPeriodGuard.EnsurePeriodOpenForAsync(employee, date)`; on `PayrollPeriodClosedException` do **not** create the deduction — emit a `PayrollAdjustmentNeeded` notification instead (mirror `AttendanceCorrectionExecutor`).

### 6.1 Configurable unpaid wage basis

Tenant-level config on `AttendancePolicy` (new columns; defaults reproduce today's behavior):

- `UnpaidDivisorBasis : DayBasis` (default `Fixed30`) → `divisorDays` = 30 / `DaysInMonth` / working-days-in-month.
- `UnpaidDailyPayableHours : decimal` (default `8`).
- `UnpaidWageComponentCodes : string?` (CSV of allowance master-data codes; default null/empty → **basic salary only**).

`IUnpaidPermissionWageResolver.MonthlyWageAsync(employeeId, date)` = `BasicSalary + Σ(eligible allowance components)`; `divisorDays` from `UnpaidDivisorBasis`. This isolates all wage-base config behind one resolver so granular component selection can grow without touching the executor. Nothing is hard-coded — 30/8/basic-only are only the column defaults.

## 7. Request-type + notification wiring

Seed a system `ATTENDANCE_PERMISSION` request type (form fields: `permissionType` [lookup `attendance-permission-types`], `date`, `fromTime`, `toTime`, `reason`, `overrideReason` [conditional]) mapped to the `Attendance.CreatePermission` effect via `SystemRequestEffects`/`RequestProvisioningService`. The existing request approval chain and workflow notifications are reused unchanged. This closes the loop: submit → approve → permission recorded + limits enforced + (unpaid) deduction.

## 8. Components & boundaries

| Unit | Responsibility | Depends on |
|------|----------------|-----------|
| `PermissionTypeRules` / `PermissionExceedBehavior` | Typed view of a type's `MetadataJson` | `SelectionScope` |
| `MasterDataDefaults` additions | Seed 5 types + `UNPAID_PERMISSION` | existing seed infra |
| `IAttendancePermissionTypeService` | Eligible types + usage for an employee | `IScopeEngine`, `AttendancePermission`, `MasterDataItem` |
| `AttendancePermissionCap` (generalized) | Pure limit evaluation → outcome | rules/limits + tallies |
| `UnpaidPermissionDeduction` | Pure wage→amount math | — |
| `IUnpaidPermissionWageResolver` | Monthly wage + divisor from config | `AttendancePolicy`, `Employee`, allowances |
| `AttendancePermissionCreateExecutor` (extended) | Write permission, enforce limits, create unpaid deduction | all of the above, `IPayrollPeriodGuard` |
| Controller endpoints | eligible-types, validate | service + evaluator |
| Request-type seed | Wire ATTENDANCE_PERMISSION → effect | request provisioning |

## 9. Testing

- **Config:** rules parse/defaults; seeding idempotent + preserves existing.
- **Eligibility:** entire-company, specific branch, specific department, specific employees, excluded employees.
- **Limits:** per-request, daily minutes, monthly minutes, requests/day, requests/month; Block vs Warn vs RequireOverride (incl. missing override reason → blocked).
- **Duplicate execution:** re-running the same request instance is a no-op (permission + deduction both dedupe).
- **Paid permission:** no payroll deduction created; attendance still excused.
- **Unpaid permission:** deduction created = approved working hours (4h ⇒ 4×hourly); configurable basis honored (non-default divisor/hours changes the amount); finalized period → flag notification, no mutation.

## 10. Implementation increments (commit + push each after tests green)

1. **Config + seed** — `PermissionTypeRules`, `PermissionExceedBehavior`, seed 5 types + `UNPAID_PERMISSION`, lookups. Tests: rules defaults, seed idempotent/preserve.
2. **Eligibility** — `IAttendancePermissionTypeService` + `eligible-types` endpoint. Tests: 5 eligibility cases.
3. **Limits + used/remaining** — generalized `AttendancePermissionCap`, usage tallies, `validate` endpoint, executor enforcement (Block/Warn/RequireOverride + mandatory reason). Tests: per-request/daily/monthly/requests limits, duplicate execution.
4. **Paid/unpaid payroll** — `UnpaidPermissionDeduction`, `IUnpaidPermissionWageResolver`, `AttendancePolicy` basis columns (+ migration), executor deduction path. Tests: paid, unpaid, configurable basis, duplicate deduction, finalized→flag.
5. **Request-type + notification wiring** — seed system ATTENDANCE_PERMISSION type mapped to the effect. Smoke tests.

Each increment: build clean, full suite green, then `git commit` + `git push`.

## 11. Out of scope (this pass)

Admin settings CRUD screens for permission types; employee submit form surfacing (eligible types + live used/remaining); multi-company/legal-entity dimension; granular per-component wage selection UI (the resolver seam supports it, no UI yet).
