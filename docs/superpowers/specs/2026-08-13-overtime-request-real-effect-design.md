# Overtime Request → Real Effect (Increment 1: Pay Branch)

**Date:** 2026-08-13
**Status:** Approved (brainstorm) — pending spec review
**Program:** "Connect the 9 essential request types to real effects" — request #4 (Overtime).
**Related:** `SystemRequestEffects.cs`, `AttendancePermissionCreateExecutor` (pattern to mirror), `AttendancePayrollSyncService` (engine overtime sync), `EffectActionCatalog`.

## Problem

The `OVERTIME_REQUEST` system request type is **mis-wired**. `SystemRequestEffects.cs:93-98`
maps it to `EffectTypes.AttendanceCorrect` ("Attendance.Correct"), passing only `date` + `reason`
and **silently discarding the `hours` field**. Approving an overtime request today recomputes an
attendance day from (absent) corrected punches — it produces **no pay and no time-off**. Overtime is
effectively a dead request.

Separately, the attendance engine already computes overtime minutes from punch overshoot
(`AttendanceRecord.OvertimeMinutes`) and can convert them to a payroll Addition via the
`includeOvertime` sync (`AttendancePayrollSyncService`), but that path is **off by default** and is
not gated by any approval.

## Goal (this increment)

Approving an overtime request creates a **real, born-Approved payroll Addition** for the approved
overtime hours, paid **exactly once**, using the Saudi-law overtime rate, posted to the correct
payroll month, and never double-paying against the engine sync.

**Out of scope (deferred to Increment 2):** the compensatory-leave ("time off in lieu") branch.
See "Deferred" below for why and how.

## Design decisions (best practice)

### D1 — Authoritative hours = the approved request (manual `hours`)
Overtime is paid **only** through the approved request, using the `hours` value on the form. The
manager/approver is the anti-abuse control (standard for request-based HR/payroll). This matches the
product's request→effect paradigm and the existing `FORM_OVERTIME` form, and works when biometric
punches are absent (field work, missing punches).

- *Rejected — pure engine-computed:* requires punches + `shift.OvertimeAllowed` for every case;
  breaks no-biometric scenarios.
- *Rejected (for v1) — manual claim capped by engine:* real anti-over-claim value but adds
  reconciliation complexity (read attendance records, handle missing data, define fallback). YAGNI
  for v1; recorded as a possible future hardening.

### D2 — Pay exactly once (double-pay guard)
The engine `includeOvertime` auto-sync and the overtime request are **mutually exclusive** payment
paths. To guarantee single payment:

- **Request-side idempotency:** dedupe on `ReferenceType = "OvertimeRequest"` + `ReferenceId =
  RequestInstanceId` (an approved request re-run creates no second Addition → `Skip`).
- **Cross-path guard:** before creating the Addition, check for an existing engine-synced overtime
  Addition covering the same employee + target month (the engine sync writes AdditionType `OVERTIME`
  with `SourceModule = "Attendance"`). If one exists, `Skip` with a clear reason
  (`"engine overtime sync already paid this period"`) rather than pay twice.
- Tenants enabling overtime requests are expected to keep `includeOvertime` **off**; the guard is a
  safety net, not a config toggle.

### D3 — Rate (KSA Labor Law Art. 107)
`amount = hours × hourlyWage × overtimeMultiplier`, rounded to 2 decimals, where:
- `hourlyWage = dailyWage / 8` on the **basic** wage (matches `PayrollFactProvider` +
  `AttendancePayrollSyncService`).
- `overtimeMultiplier` reads `CalcSettingsJson.attendanceRates.overtimeMultiplier`
  (`PayrollCalcSettings`), **default 1.5** = 150% (Art. 107).

A new single-employee wage resolver, `IOvertimeWageResolver` (impl `OvertimeWageResolver`), returns
the `hourlyWage` for an employee, mirroring `IUnpaidPermissionWageResolver`. This is the one genuine
net-new computation seam (the existing formula lives inside the payroll-run fact provider, which
needs a `PayrollDefinitionVersion` + `PayrollPeriod` — not available at approval time).

### D4 — Which payroll month it posts to
The Addition posts to `TargetPeriodYear/Month = date.Year/Month` of the overtime `date` (mirrors
`AttendancePermissionCreateExecutor`).

- If that period is **already finalized/closed** (`IPayrollPeriodGuard.EnsurePeriodOpenForAsync`
  throws): do **not** mutate. Instead emit the existing `PayrollAdjustmentNeeded` `Notification`
  (same fallback as `AttendancePermissionCreateExecutor` / `AttendanceCorrectionExecutor`), so HR
  handles it as a manual adjustment. No money is posted silently into a closed period.

## Components

### New: `OvertimeWageResolver : IOvertimeWageResolver` (HR.Application / Finance)
- `Task<decimal> ResolveHourlyWageAsync(Guid employeeId, CancellationToken ct)` →
  `basicMonthly / 30 / 8` daily→hourly (reuse the existing DayBasis 30 + 8 payable-hours defaults;
  same basis the unpaid-permission resolver uses). Reads the employee's basic salary from the same
  source `UnpaidPermissionWageResolver` uses.

### New: `OvertimeAdditionExecutor : IEffectExecutor`
- `EffectType = "Overtime.CreateAddition"` (new constant in `EffectTypes`).
- Auto-registered via assembly DI scanning (`AddEffectExecutorsFromAssembly`) — **no DI edit**.
- Placed in the same module assembly as `AttendancePermissionCreateExecutor` so scanning finds it.
- `ExecuteAsync`:
  1. Read payload: `date` (Date, required), `hours` (Dec, required, > 0), `reason` (Str).
  2. Idempotency: if an Addition with `ReferenceType="OvertimeRequest"` + `ReferenceId=RequestInstanceId`
     exists → `Skip`.
  3. Double-pay guard (D2): if an engine-synced `OVERTIME` Addition covers this employee + target
     month → `Skip` with reason.
  4. Resolve AdditionType `OVERTIME` master-data id (already seeded in `MasterDataDefaults`); if
     missing → throw `NonRetryableEffectException` (mirrors the unpaid-permission "type unseeded" guard).
  5. Period guard (D4): `EnsurePeriodOpenForAsync(employeeId, date)`; on throw → emit
     `PayrollAdjustmentNeeded` notification + `Skip` (no mutation).
  6. `amount = round(hours × ResolveHourlyWageAsync × overtimeMultiplier, 2)`.
  7. Add a born-`Approved` `PayrollTransaction { Kind = Addition, EmployeeId, TypeId, Amount,
     EffectiveDate = date, TransactionDate = date, TargetPeriodYear/Month, SourceModule = "Overtime",
     ReferenceType = "OvertimeRequest", ReferenceId = RequestInstanceId, Origin = System }`.
     Do **not** call `SaveChanges` (the completion engine commits).
  8. Return `EffectExecutionResult.Ok`.

### Changed: `EffectActionCatalog`
- Add an `EffectActionDescriptor` for `Overtime.CreateAddition`: inputs `date` (Date, required),
  `hours` (Number, required), `reason` (TextArea, optional); `ExecutionMode = Transactional`
  (an approval must be able to roll back); `SupportedTriggers = FinalOnly`; `RequiredPermissions`
  = the manual payroll-addition create permission (same authority as creating an Addition by hand).

### Changed: `SystemRequestEffects.cs:93-98`
- Re-map `OVERTIME_REQUEST` from `AttendanceCorrect` → `Overtime.CreateAddition`, mapping form
  fields `date` (from `startDate`), `hours`, `reason`. The stale `Attendance.Correct` wiring
  (which drops `hours`) is removed.

### Changed: `RequestProvisioningService.CurrentSeedVersion`
- Bump (6 → 7). Provisioning's `ReconcileRequiredEffects` swaps the overtime request's required
  effect to the new one on re-provision. Non-destructive (merge, preserves tenant remaps — the
  same reconcile path SP2 used).

### Unchanged
`FORM_OVERTIME` already has `startDate` (Date), `hours` (Number), `reason` (TextArea) — no form
change. `AdditionType "OVERTIME"` already seeded — no master-data change. No schema migration
(config rows + reused `PayrollTransaction` table).

## Data flow

```
Employee submits OVERTIME_REQUEST (date, hours, reason)
  → manager-approval workflow (wfManager, unchanged)
  → FinalApproval → CompletionEngine runs required effects
      → OvertimeAdditionExecutor:
           idempotent? engine already paid? period closed?  → Skip / notify
           else → PayrollTransaction(Addition, OVERTIME, amount) [Approved]
  → appears on the target month's payroll as an approved addition
```

## Error handling

| Case | Behavior |
|------|----------|
| `hours` ≤ 0 or missing | `ValidationException` (bad input) — rolls back approval |
| AdditionType `OVERTIME` unseeded | `NonRetryableEffectException` — rolls back, flagged for attention |
| Duplicate (same request re-run) | `Skip` (idempotent) |
| Engine sync already paid the period | `Skip` + reason (no double-pay) |
| Target payroll period finalized | No mutation; `PayrollAdjustmentNeeded` notification + `Skip` |
| Any executor throw | Completion tx rolls back (per `IEffectExecutor` contract) |

## Testing (TDD)

Domain/unit tests (mirror `AttendancePermissionCreateExecutorTests` harness — InMemory
`ApplicationDbContext` + fakes):
1. Approved overtime → one born-Approved `OVERTIME` Addition with `amount = hours × hourlyWage × 1.5`.
2. Amount uses the configured multiplier (non-default, e.g. 2.0) from `attendanceRates`.
3. Posts to `TargetPeriodYear/Month` of the overtime date.
4. Idempotent: re-running the same request instance creates no second Addition (`Skip`).
5. Double-pay guard: an existing engine-synced `OVERTIME` addition for the period → `Skip`.
6. Finalized period → no Addition; a `PayrollAdjustmentNeeded` notification is emitted.
7. `hours` ≤ 0 → `ValidationException`.
8. AdditionType `OVERTIME` unseeded → `NonRetryableEffectException`.
9. `OvertimeWageResolver` returns `basic / 30 / 8`.

Full suite must stay green (currently 744 pass / 62 skip).

## Deferred — Increment 2: Compensatory-leave branch

Not built now, and deliberately so: the codebase has **no leave-credit path** today
(`LeaveCreateApprovedLeaveExecutor` only *deducts* balance; there is no accrual/credit executor),
**no** seeded "Compensatory / Time-in-Lieu" leave type, and no hours→days conversion rule. That is a
whole net-new leave-credit subsystem and merits its own increment. Increment 2 will add: a
`compensationType` form field (Pay | CompLeave), a comp-leave `LeaveType` seed, an hours→days rule,
and a leave-credit executor writing `LeaveBalanceTransaction { Type = Accrual, Delta = +days }`.

## Deploy (user-gated, after merge)

No schema migration. Steps: API zip-redeploy to `hrcloud-api-v4xd`; re-provision tenant(s) so
SeedVersion 6→7 re-wires the overtime effect; behavioral verify (submit overtime → approve →
confirm an approved `OVERTIME` Addition on the target month).
