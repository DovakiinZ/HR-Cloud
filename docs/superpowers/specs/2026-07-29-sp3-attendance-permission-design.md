# SP3 — Attendance Permission (استئذان) — Design Spec

**Date:** 2026-07-29
**Program:** "Connect 9 essential request types to real effects" (SP3, after SP2 Attendance Correction).
**Status:** Design approved (Approach A). Ready for implementation planning.

## 1. Problem & intent

An **Attendance Permission** (استئذان) is an employee's request to arrive late or leave early for
part of a working day (e.g. a doctor's appointment). When **approved**, the covered minutes must be
**excused**: the late/shortage penalty for that window is waived, the employee still counts as
`Present`, and payroll must **not** deduct for those minutes.

Today there is **no permission/excuse concept anywhere** in the code. Attendance penalties
(`LateMinutes`, `ShortageMinutes`) are purely *calculated* from punches vs. the assigned shift, with
no mechanism to waive them. SP3 introduces that mechanism the same way SP2 did for corrections:
**feed the real calculation engine a durable input — never fake the penalty numbers.**

### Approved decisions (brainstorm)
- **Semantics:** excuse the late/early minutes; day stays `Present`; payroll deducts less.
- **Input model:** a **time window** (date + from `HH:mm` + to `HH:mm` + reason). Excused minutes =
  overlap of the window with the effective shift.
- **Policy:** enforce a **configurable monthly cap** per employee (count and/or minutes), with a
  Block-or-Warn mode.
- **Persistence (Approach A):** a first-class `AttendancePermission` entity that the calculation
  engine consults live, so the excuse is durable across recalculation, punch sync, and regeneration.

## 2. Architecture overview

```
Employee submits ATTENDANCE_PERMISSION request (date, from, to, reason)
   → SP1 notifications (Submitted→Requester, StepAssigned→CurrentApprover)
   → manager approves (Rejected/Returned/FinalApproved notifications)
   → FinalApproval fires Attendance.Permission effect
       → AttendancePermissionExecutor:
            validate → idempotency skip → monthly-cap check → finalized-payroll guard
            → write AttendancePermission row → RecalcAsync(employee, date)
   → AttendanceCalculationService.Calculate() loads that day's approved permissions
       → subtracts window∩shift overlap from late/shortage BEFORE deciding status
   → AttendanceRecord persists reduced Late/Shortage (+ ExcusedMinutes) → payroll sync deducts less
```

The excuse is **recomputed on every recalc** because the calculator always consults the
`AttendancePermission` rows — this is what makes it durable across recalculation, punch
synchronization, and attendance regeneration (an explicit requirement).

## 3. Domain model

### 3.1 `AttendancePermission` (new entity, tenant-scoped)

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `TenantId` | Guid | tenant isolation (TenantEntity) |
| `EmployeeId` | Guid | subject |
| `Date` | DateTime (date) | naive local date; `TODO(tz)` (kept naive like SP2) |
| `FromMinutes` | int | window start, minutes-from-midnight |
| `ToMinutes` | int | window end, minutes-from-midnight (may exceed 1440 for overnight) |
| `ExcusedMinutes` | int | snapshot of window∩shift computed at approval; used by the cap tally + display |
| `Reason` | string | from the form |
| `RequestInstanceId` | Guid | provenance → idempotency + audit link |
| `Source` | string | new `AttendanceSources.AttendancePermission` constant |
| `CreatedAt` | DateTime | audit |
| `CreatedByUserId` | Guid? | actor (nullable) |

Rows are **immutable** (audit history preserved). No status column in scope — cancellation-reversal is
scoped out (see §9).

### 3.2 `AttendanceRecord` addition
Add one **output** column `ExcusedMinutes` (int, default 0) so attendance/payslip views can explain
"raw late 120, excused 120". Written by `RecalcAsync` from the calc result. Not a source of truth —
the `AttendancePermission` rows are.

### 3.3 `AttendanceSources`
Add constant `AttendancePermission = "AttendancePermission"`.

## 4. Effect wiring (mirrors SP2)

- `EffectTypes.AttendancePermission = "Attendance.Permission"`.
- `EffectActionCatalog` descriptor: module `Attendance`, trigger `FinalApproval` only, mode
  `Transactional`, required permission `Attendance.Edit`. Inputs:
  - `date` (required, `FieldOrContext`)
  - `from` (required, `FieldContextOrConstant`, HH:mm)
  - `to` (required, `FieldContextOrConstant`, HH:mm)
  - `reason` (required, `FieldContextOrConstant`)
- `SystemRequestEffects["ATTENDANCE_PERMISSION"]`:
  ```csharp
  Transactional(EffectTypes.AttendancePermission, Map(
      ("date",   Field("startDate")),
      ("from",   Field("fromTime")),
      ("to",     Field("toTime")),
      ("reason", Field("reason"))));
  ```

## 5. Calc-engine integration (durable core)

`AttendanceCalculationService.Calculate(...)` gains an optional parameter:
`IReadOnlyList<PermissionWindow>? permissions = null` where `PermissionWindow` carries
`FromMinutes`/`ToMinutes`. `RecalcAsync` loads the day's approved `AttendancePermission` rows for the
employee, maps them to windows, and passes them in. The calculator stays a **pure function** (fully
unit-testable with no DB).

### Algorithm (applied *before* the status decision)
Given raw `LateMinutes` and `ShortageMinutes` computed as today, and the shift span
`[shiftStart, shiftEnd]` (with `shiftEnd += 1440` when the shift is overnight and `shiftEnd <= shiftStart`):

1. **Merge** all permission windows and clamp to the shift span → set `W` (merging dedupes
   **overlapping permissions** so minutes are never double-counted).
2. `excusedLate = |W ∩ [shiftStart, checkIn]|`, capped at raw `LateMinutes`.
   (the tardy interval; zero if the employee was on time.)
3. `excusedShortage = |W ∩ shiftSpan| − excusedLate`, capped at raw `ShortageMinutes`.
   (covers **early departure** and **temporary exit** — any unworked shift minutes the window covers.)
4. `LateMinutes' = raw − excusedLate`; `ShortageMinutes' = raw − excusedShortage`.
5. Recompute status with the reduced values → returns to `Present` when `ShortageMinutes' == 0`
   (and late is within threshold).
6. `ExcusedMinutes = excusedLate + excusedShortage` returned in the result and written to the record.

Overnight windows use the same 1440 offset so `from`/`to` on the shift date compare correctly.

Because payroll's attendance sync reads `LateMinutes`/`ShortageMinutes` off the record, deductions
shrink automatically — **no payroll-side change**.

## 6. Executor behavior (`AttendancePermissionExecutor`, mirrors SP2 hardening)

On `FinalApproval`:
1. **Read + validate:** `date`, `from`, `to`, `reason`. Reason required; `from`/`to` valid `HH:mm`;
   `from < to`. Throw `ValidationException` on failure (rolls back).
2. **Idempotency:** if an `AttendancePermission` already exists with
   `RequestInstanceId == context.RequestInstanceId` → return `Skip("AlreadyApplied")`.
3. **Monthly-cap check** (§7): compute the employee's month usage; if adding this permission exceeds a
   configured cap in **Block** mode → throw `ValidationException`. In **Warn** mode, proceed.
4. **Finalized-payroll guard:** if the permission's month payroll period is finalized, block unless the
   actor holds `Payroll.Run.Amend`; if authorized, apply and emit a bell `Notification` payroll-
   adjustment signal (no money posted directly) — identical to SP2.
5. **Persist:** compute `ExcusedMinutes` (window∩shift snapshot), write the `AttendancePermission`
   row stamped with `Source` + `RequestInstanceId` + actor.
6. **Recalc:** trigger `RecalcAsync(employee, date)` so the record's Late/Shortage/Status/ExcusedMinutes
   update immediately.
7. Return `Ok` with before/after state for audit.

## 7. Monthly cap policy

Extend the existing per-tenant attendance policy settings (the same settings object the calculator
already receives as `AttendancePolicySettings? policy`) with:

| Setting | Type | Meaning |
|---|---|---|
| `PermissionMaxPerMonth` | int? | max approved permissions/employee/month (null = unlimited) |
| `PermissionMaxMinutesPerMonth` | int? | max excused minutes/employee/month (null = unlimited) |
| `PermissionCapMode` | enum `Block`\|`Warn` | Block rejects the over-cap effect; Warn only flags |

- **Tally:** count (and sum `ExcusedMinutes` of) this employee's `AttendancePermission` rows whose
  `Date` is in the same calendar month as the requested date, **plus** the incoming one.
- **Authoritative enforcement:** in the executor (step 3). Block → `ValidationException`.
- **UX preview:** a lightweight `POST /api/attendance/permissions/preview` (employeeId, date,
  from, to) returns `{ usedCount, usedMinutes, wouldExceed, mode }` so the request form can warn or
  block *before* submit. This is a nicety; the executor remains the source of truth.

## 8. Notifications & seeding

- **Notifications:** add 5 `ATTENDANCE_PERMISSION` rules to `SystemWorkflowNotificationRules`
  (Submitted→Requester, StepAssigned→CurrentApprover, Rejected→Requester, Returned→Requester,
  FinalApproved→Requester), Arabic + English copy mirroring correction. No new dispatch code — rides
  the SP1 dispatcher.
- **Request type + form:** add `ATTENDANCE_PERMISSION` ("استئذان" / "Attendance Permission") to
  `RequestSeeder` with form fields: `startDate` (Date, required), `fromTime` (Text HH:mm, required),
  `toTime` (Text HH:mm, required), `reason` (TextArea, required). Manager-approval workflow like
  correction.
- **Provisioning:** bump `RequestProvisioningService.CurrentSeedVersion 5 → 6`. Reconcile on provision
  creates the new type/form, adds the required effect, seeds the notification rules, and (for existing
  tenants) backfills them. No dynamic frontend work — the standard dynamic form renders it.

## 9. Migration

One EF migration:
- `attendance_permissions` table (§3.1).
- `AttendanceRecord.ExcusedMinutes` column (default 0).
- The three permission-policy setting columns (§7) on the attendance settings table.

## 10. Testing

**Pure calculator unit tests** (no DB):
- **Late arrival** — window covers the tardy interval → `LateMinutes` reduced, `Present`.
- **Early departure** — window covers `[checkOut, shiftEnd]` → `ShortageMinutes` reduced, `Present`.
- **Temporary exit** — mid-shift window reduces shortage minutes.
- **Overnight shift** — window math correct across the 1440 boundary.
- **Overlapping permissions** — two overlapping windows merge; excused minutes not double-counted.
- Partial cover — window smaller than the penalty leaves a residual penalty.

**Executor / integration tests:**
- **Idempotency** — re-running the same request skips (one row, no double recalc).
- **Finalized-payroll guard** — blocked without `Payroll.Run.Amend`; applied + signal with it.
- **Durability** — after applying a permission, a subsequent `RecalcAsync` / punch sync still yields
  the excused result.
- **Monthly cap** — Block rejects the over-cap request; Warn allows it.

Target the same green bar as SP2 (Finance + Platform suites).

## 11. Scoped out (future SPs)

- Cancellation-reversal of an approved permission (remove row + recalc).
- Permission balance accruals beyond the simple monthly cap.
- Timezone correctness (kept naive, `TODO(tz)`).

## 12. Commit discipline

Small, focused commits — one logical change each; commit + push (origin + sanad) after each stable,
tested piece; never commit broken/untested code. Deployment is user-gated (API redeploy + apply the
migration + reprovision tenant 5→6 + behavioral verify), following the SP2 mechanics.
