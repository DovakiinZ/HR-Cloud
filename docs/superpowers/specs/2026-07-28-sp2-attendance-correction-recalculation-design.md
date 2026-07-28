# SP2 — Attendance Correction: Real Punch Correction + Recalculation

**Date:** 2026-07-28
**Program:** Connect the 9 essential request types to real effects (SP2 of the sequence; SP1 = notification framework, done).
**Status:** Design approved; ready for implementation plan.

## Problem

The `ATTENDANCE_CORRECTION` request type does not actually correct attendance. Its executor
(`AttendanceCorrectionExecutor`, `backend/src/HR.Modules/Attendance/Completion/AttendanceCorrectionExecutor.cs`)
brute-forces the affected day to `Status = Present, LateMinutes = 0, ShortageMinutes = 0`. It never
reads the employee's real punches, never recalculates against the shift, never writes an audit row,
and never keeps payroll consistent. Approving a correction therefore silently erases legitimate
late/shortage penalties instead of recomputing them from what actually happened.

The form matches this shallow behavior: it captures only `date` (required) and `reason` (required) —
there is nowhere to enter the corrected punch times.

The real machinery already exists and is reused elsewhere:
- `IAttendanceService.CorrectAsync(Guid recordId, CorrectAttendanceRequest req)` — writes an
  `AttendanceCorrection` audit row (old/new punches) and calls `RecalcAsync`, which invokes
  `IAttendanceCalculationService.Calculate(...)` to recompute late/shortage/overtime/status from the
  corrected punches (`AttendanceService.cs:261-311`).
- `IAttendanceService.AddManualPunchAsync(ManualPunchRequest req)` — creates a record for a day that
  has none, with audit + recalc (`AttendanceService.cs:231-259`).
- Both request DTOs take `CheckIn` / `CheckOut` as `string "HH:mm"` (`AttendanceDtos.cs:123-136`).

## Goal

On approval, `ATTENDANCE_CORRECTION` recomputes the day's attendance from the corrected punches,
records an audit trail, and notifies through the SP1 framework — so attendance reflects what HR
approved, and penalties are a *consequence of correct punches*, not a hard-coded zero.

## Non-goals (explicitly out of scope)

- **Immediate payroll re-sync.** The recalculated `AttendanceRecord` is the source of truth; the next
  payroll run / attendance re-sync picks up the corrected values. No coupling to payroll-run state.
- **The tenant-less login bug** (admin-created users get `tenant_id = Guid.Empty`) — deferred; see
  memory `tenant-less-login-bug`.
- No changes to `MISSING_PUNCH`, leave, or any other request type.

## Design

### A. Form — `FORM_ATTENDANCE_CORRECTION`

Keep `date` (Date, required) and `reason` (TextArea, required). Add two fields, mirroring the
`MISSING_PUNCH` form (`RequestSeeder.cs:374-375`):

- `checkIn` — `FieldType.Text`, optional, placeholder `08:00` (HH:mm)
- `checkOut` — `FieldType.Text`, optional, placeholder `17:00` (HH:mm)

**Validation:** at least one of `checkIn` / `checkOut` must be provided (plus `reason`). A blank punch
means "leave that punch unchanged" — `CorrectAsync`/`AddManualPunchAsync` already fall back to the
existing value when a punch string is null (`AttendanceService.cs:250-251, 267-268`). This covers the
most common real case (forgot one punch). Where the ≥1-punch rule is enforced is decided in the plan
(submit-time form validation and/or executor guard); the executor MUST be safe regardless.

### B. Executor rewrite — `AttendanceCorrectionExecutor`

Replace the raw `_db` mutation with a route through `IAttendanceService`:

1. Read `date` (`ctx.Date("date")`), `reason` (`ctx.Str("reason")`), `checkIn` (`ctx.Str("checkIn")`),
   `checkOut` (`ctx.Str("checkOut")`).
2. Look up the `AttendanceRecord` for `(ctx.EmployeeId, date)`.
   - **Found** → `CorrectAsync(rec.Id, new CorrectAttendanceRequest { CheckIn, CheckOut, Reason })`.
   - **Not found** → `AddManualPunchAsync(new ManualPunchRequest { EmployeeId = ctx.EmployeeId, Date = date, CheckIn, CheckOut, Notes = reason })`
     (note `ManualPunchRequest` uses `Notes`, whereas `CorrectAttendanceRequest` uses `Reason` —
     `AttendanceDtos.cs:123-137`).
3. Return `EffectExecutionResult.Ok(targetEntityType: "AttendanceRecord", targetRecordId: <id>, summary: ...)`
   where the summary reflects the recomputed late/shortage (proving real recalculation). Throw to roll
   back on failure (record not creatable, service throws).
- Remains **Transactional** (recalc is synchronous and fast).
- Dependency change: inject `IAttendanceService` instead of manipulating `AttendanceRecord` directly.
- The old hard-coded `Status = Present, LateMinutes = 0, ShortageMinutes = 0` block is deleted.

Both service methods internally set `Source = AttendanceSources.AttendanceCorrection`, so provenance is
preserved.

### C. Effect catalog + config

- `EffectActionCatalog` (`EffectActionCatalog.cs:88-103`): add `checkIn` and `checkOut` inputs to the
  `AttendanceCorrect` descriptor (optional, `FieldOrContext`). Update its Ar/En label + description —
  it no longer "clears late/shortage"; it "recomputes attendance from the corrected punches."
- `SystemRequestEffects["ATTENDANCE_CORRECTION"]` (`SystemRequestEffects.cs:70-75`): add the two new
  input mappings (`checkIn` ← Field("checkIn"), `checkOut` ← Field("checkOut")) alongside the existing
  `date` and `reason`. Effect stays a single Transactional effect on FinalApproval.

### D. Notifications

Add 5 `ATTENDANCE_CORRECTION` rules to `SystemWorkflowNotificationRules` (`SystemWorkflowNotificationRules.cs`),
mirroring `LEAVE_REQUEST`:

| SystemKey | Event | Recipient | Copy (theme) |
|---|---|---|---|
| `ATTENDANCE_CORRECTION:Submitted:Requester` | Submitted | Requester | "correction request received / under review" |
| `ATTENDANCE_CORRECTION:StepAssigned:CurrentApprover` | StepAssigned | CurrentApprover | "a correction awaits your approval, from {{Employee.FullName}}" |
| `ATTENDANCE_CORRECTION:Rejected:Requester` | Rejected | Requester | "correction rejected" |
| `ATTENDANCE_CORRECTION:Returned:Requester` | Returned | Requester | "correction returned for changes" |
| `ATTENDANCE_CORRECTION:FinalApproved:Requester` | FinalApproved | Requester | "correction approved and applied" |

No dispatcher code — SP1 already wires the 6 RequestEngine events. Rules use the same `{{Request.Number}}`
/ `{{Employee.FullName}}` tokens, verified working in SP1.

### E. Provisioning — `CurrentSeedVersion` 4 → 5

New tenants get everything from the seeder. Existing tenants are the risk: today's reconcile
(`RequestProvisioningService.cs`) only **adds missing effects**, backfills field *classification*, and
seeds notification rules — it does **not** add new form fields and it deliberately leaves existing
effect *config* untouched. So on the 4→5 bump we extend the reconcile, scoped to
**system-owned, un-customized `ATTENDANCE_CORRECTION`** only:

1. **Form fields:** ensure the shipped system fields exist on the form by `Code` — add `checkIn` /
   `checkOut` if missing. Never touch tenant-added fields; never remove anything.
2. **Effect config:** refresh the system `AttendanceCorrect` effect's input mapping to the shipped set
   when the effect is still `IsSystem`/required and its config has not been customized. (If a tenant
   remapped it, leave it alone.)
3. **Notification rules:** the existing `ReconcileWorkflowNotificationRules` already handles the new
   `ATTENDANCE_CORRECTION` entries non-destructively.

The one live Azure tenant has not customized this type, so the upgrade applies cleanly. This is the
part of the build that goes beyond current reconcile behavior and needs the most care + tests.

### F. Testing (TDD)

Executor unit tests (the core):
- **Found path:** existing record with late punches → after correction with on-time punches,
  `LateMinutes`/`ShortageMinutes` are recomputed (e.g. late → 0 only because the corrected punch is on
  time), and an `AttendanceCorrection` audit row exists. Prove it calls `IAttendanceService`, not raw zeroing.
- **Recalc-is-real test:** corrected punches that are *still late* produce **non-zero** `LateMinutes`
  (the anti-regression against the old "always zero" behavior).
- **Not-found path:** no record for the date → `AddManualPunchAsync` creates one + recalc.
- **Validation:** ≥1 punch enforced (whichever layer owns it); reason required.
Provisioning tests:
- 4→5 bump on a system-owned, un-customized type adds `checkIn`/`checkOut` fields, refreshes effect
  config, and seeds the 5 notification rules — idempotent on re-run; a customized type is left untouched.
Notification seeding test: `SystemWorkflowNotificationRules.For("ATTENDANCE_CORRECTION")` returns 5 rules.

### G. Migration / deploy

- No schema migration expected — form fields, effect config, and notification rules are all data rows
  created by the seeder/reconcile; `AttendanceCorrection` and `AttendanceRecord` tables already exist.
- Deploy = API zip-redeploy, then re-provision the tenant (SeedVersion 4→5) so existing rows upgrade.
- Verify behaviorally (reuse the SP1 admin-linked-to-self-managed-employee trick, since employee
  self-service is blocked by the deferred login bug): submit a correction with a late punch → approve
  → confirm the `AttendanceRecord` shows recomputed late minutes (not zero) + an `AttendanceCorrection`
  audit row + the 3 lifecycle bells fire.

## Reused components (do not rebuild)

- `IAttendanceService.CorrectAsync` / `AddManualPunchAsync` / `RecalcAsync` — recompute + audit.
- `IAttendanceCalculationService.Calculate` — pure late/shortage/overtime/status computation.
- SP1 notification dispatcher — fires all lifecycle events already.
- `RequestProvisioningService` reconcile pattern — extended, not replaced.

## Open risks

1. **Section E** (existing-tenant form-field + effect-config reconcile) is new behavior; keep it
   narrowly scoped to system-owned, un-customized types and cover it with idempotency tests.
2. Behavioral verification depends on the admin-link workaround until the tenant-less login bug is fixed.
