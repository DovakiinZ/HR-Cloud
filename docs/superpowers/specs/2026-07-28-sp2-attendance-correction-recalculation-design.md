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

- **Immediate payroll re-sync for OPEN periods.** For a non-finalized month the recalculated
  `AttendanceRecord` is the source of truth; the next payroll run / attendance re-sync picks it up. No
  coupling to open-run state. (Finalized months ARE handled — see §H.)
- **Auto-posting financial delta adjustments.** When a correction lands on a finalized month we *block
  or signal* (§H); the attendance module never computes/posts payroll money itself. Posting the actual
  adjustment stays in the payroll module's amend flow.
- **System-wide timezone migration.** The attendance engine is timezone-naïve everywhere; SP2 stays
  consistent with it and only validates/flags (§I). A real UTC↔local migration is a separate task.
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

Replace the raw `_db` mutation with a route through `IAttendanceService`, in this exact order:

1. **Read + validate inputs.** `date` (`ctx.Date("date")`), `reason` (`ctx.Str("reason")`),
   `checkIn` (`ctx.Str("checkIn")`), `checkOut` (`ctx.Str("checkOut")`). Validate: at least one punch
   present; each provided punch matches `HH:mm` (24h) — see §I. Invalid → throw a clear
   `ValidationException` (rolls the effect back). This closes the silent-null gap in `CombineTime`.
2. **Idempotency guard** (§J). If an `AttendanceRecord` already exists for `(ctx.EmployeeId, date)` with
   `Source = AttendanceSources.AttendanceCorrection` **and** `ReferenceId = ctx.RequestInstanceId`, this
   effect already ran → return `EffectExecutionResult.Skip("AlreadyApplied", ...)`. No double-apply.
3. **Finalized-payroll guard** (§H). Determine whether `date`'s payroll period is finalized for this
   employee. If finalized and the actor is **not** authorized → throw a clear "payroll period finalized"
   error (blocks; nothing diverges). If finalized and authorized → proceed **and** emit the
   payroll-adjustment signal (§H).
4. **Apply the correction** via `IAttendanceService` (recompute + audit):
   - Look up the `AttendanceRecord` for `(ctx.EmployeeId, date)`.
   - **Found** → `CorrectAsync(rec.Id, new CorrectAttendanceRequest { CheckIn, CheckOut, Reason })`.
   - **Not found** → `AddManualPunchAsync(new ManualPunchRequest { EmployeeId = ctx.EmployeeId, Date = date, CheckIn, CheckOut, Notes = reason })`
     (`ManualPunchRequest` uses `Notes`; `CorrectAttendanceRequest` uses `Reason` — `AttendanceDtos.cs:123-137`).
   - Set the corrected record's `ReferenceId = ctx.RequestInstanceId` (drives the §J idempotency guard
     on re-runs) and confirm `Source = AttendanceSources.AttendanceCorrection` (both service methods set
     Source; ReferenceId must be set by the executor since these service methods don't take it).
5. **Return** `EffectExecutionResult.Ok(targetEntityType: "AttendanceRecord", targetRecordId: <id>, summary: ...)`
   with the recomputed late/shortage in the summary (proves real recalculation, not zeroing). Throw to
   roll back on any failure.

- Remains **Transactional** (recalc + guards are synchronous and fast); the whole effect is one unit —
  a guard failure rolls everything back.
- Dependency change: inject `IAttendanceService`, `IPayrollPeriodGuard`, a permission resolver (for the
  §H authorization check), and the notification/signal service — instead of manipulating
  `AttendanceRecord` directly.
- The old hard-coded `Status = Present, LateMinutes = 0, ShortageMinutes = 0` block is deleted.

Overnight shifts, effective-shift-for-date, and policy are all inherited from the engine
(`AttendanceCalculationService.cs:92-93` adds 24h for checkout < checkin; `ShiftResolver.Resolve`
selects the date-effective shift; `LoadPolicyAsync` uses the active tenant policy). SP2 adds no shift
math — it only feeds corrected punches into the existing engine.

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

### H. Finalized-payroll handling (block; if authorized, correct + signal)

Detection: `IPayrollPeriodGuard.EnsurePeriodOpenForAsync(employeeId, date)` throws
`PayrollPeriodClosedException` when an immutable payroll run (`Approved`/`Executing`/`Completed`/
`Locked`/`Archived` per `PayrollRunStateMachine.IsImmutable`) covers that date's period
(`PayrollPeriodGuard.cs:27-69`). The executor uses this as the finalized-period signal — either by
catching the exception or via a small non-throwing companion check `IsPeriodOpenForAsync` added
alongside it (decided in the plan; a boolean check reads cleaner than exception-as-control-flow).

Authorization: "explicitly authorized" = the completion **actor** (`ctx.ActorUserId`, the final
approver) holds `Payroll.Run.Amend`. The executor resolves the actor's effective permissions via the
permission resolver and checks for that permission.

Behavior when `date`'s period is finalized:
- **Not authorized →** throw a clear domain error ("payroll period finalized for {month}; correcting it
  requires payroll-amend authorization"). The effect rolls back; the attendance record is NOT changed;
  nothing silently diverges. (Surfaces to the approver as a failed completion.)
- **Authorized →** apply the attendance correction (§B step 4) AND emit an explicit
  **payroll-adjustment signal**: a notification to the payroll role/actor carrying employee, date, the
  before→after late/shortage minutes, and the request number — "attendance corrected after payroll was
  finalized; a payroll adjustment may be required." The signal is the divergence made explicit; the
  attendance module does NOT compute or post any money. Payroll posts the adjustment through its own
  amend flow (`IPayrollRunAmendmentService` / `IPayrollTransactionReversalService`), unchanged.

For an OPEN period (the common case) none of this fires — correction applies and the next payroll run
picks up the recomputed values.

### I. Timezone & HH:mm validation

The attendance engine is timezone-naïve: `CombineTime` interprets `"HH:mm"` as a UTC-kind wall-clock
time (`AttendanceService.cs:393-399`); tenant `CompanySettings.Timezone` (default `"Asia/Riyadh"`)
exists but is never consulted; there is no employee-level timezone. SP2 stays **consistent** with this —
corrected punches use the same wall-clock handling as device punches, so corrections never desync from
real punches. What SP2 adds:
- **HH:mm validation** at the executor boundary (and mirrored as submit-time form validation): reject
  anything not matching a 24h `HH:mm` before it reaches `CombineTime`'s silent null. Clear error message.
- **Tenant timezone as the date reference:** where the executor needs "which day" (e.g. defaulting or
  bounding the correction date), it resolves `CompanySettings.Timezone` rather than hard-coding UTC, so
  the seam exists for the future system-wide migration.
- A `// TODO(tz):` marker + a note in this spec that full UTC↔local conversion is a separate
  cross-cutting task (changing only correction would introduce an offset bug).

### J. Idempotency (no duplicate execution)

The completion engine has **no run-level dedupe for transactional effects** — a duplicate final-approval
callback / re-drain would create a second `CompletionRun` and re-execute
(`CompletionEngine.cs:46-97`). SP2 makes the *executor* idempotent so a second run is a safe no-op:
- On entry (after validation), query for an existing `AttendanceRecord` with
  `EmployeeId = ctx.EmployeeId`, `Date = date`, `Source = AttendanceSources.AttendanceCorrection`,
  `ReferenceId = ctx.RequestInstanceId`. If found → `EffectExecutionResult.Skip("AlreadyApplied")`.
- The apply step (§B.4) sets `ReferenceId = ctx.RequestInstanceId` so this guard matches on re-run.
This is the effect-level guard the investigation recommends; a broader engine-level run guard is noted
as a separate improvement, out of SP2 scope.

### K. Audit identities preserved

All three identities are already captured and must remain recoverable (assert with a test, add nothing
new unless a gap appears):
- **Requester:** `RequestInstance.CreatedBy` / `FormSubmission.SubmittedById`.
- **Approver:** `RequestApproval.DecidedByUserId` (per step).
- **Executor/final approver:** `CompletionRun.FinalApproverUserId`; the effect's `AuditEntry.UserId`
  (from `ICurrentUserService`) + `CompletionEffect.ExecutorName`/`TargetRecordId`. The
  `AttendanceCorrection` row records old/new punches + reason. The completion audit action is tagged
  `Completion:Attendance.Correct`.

### F. Testing (TDD)

Executor unit tests (the core):
- **Found path:** existing record with late punches → after correction with on-time punches,
  `LateMinutes`/`ShortageMinutes` are recomputed to 0 *because the corrected punch is on time*, and an
  `AttendanceCorrection` audit row exists. Prove it calls `IAttendanceService`, not raw zeroing.
- **Recalc-is-real (anti-regression):** corrected punches that are *still late* produce **non-zero**
  `LateMinutes` — the guard against the old "always zero" behavior.
- **Missing-punch / not-found path:** no record for the date → `AddManualPunchAsync` creates one +
  recalc. Also: single-punch correction (only `checkOut` provided) leaves the existing check-in intact.
- **Overnight shift:** with an overnight shift (e.g. 22:00→06:00) and a corrected checkout past midnight,
  the recomputed worked-minutes cross midnight correctly (no negative/absurd shortage) — locks in the
  inherited `+24h` engine behavior.
- **HH:mm validation:** `"25:00"`, `"9am"`, `""`-both → rejected with a clear error, no silent null.
- **≥1-punch + reason:** both punches blank → rejected; reason required.
- **Idempotency / duplicate callbacks:** running the same effect twice for the same
  `RequestInstanceId` applies the correction once; the second run returns `Skip("AlreadyApplied")` and
  does not create a second `AttendanceCorrection` row or re-mutate the record.
- **Finalized payroll — blocked:** date in a locked period, actor lacks `Payroll.Run.Amend` → throws;
  the `AttendanceRecord` is unchanged.
- **Finalized payroll — authorized:** same date, actor has `Payroll.Run.Amend` → correction applies AND
  a payroll-adjustment signal is emitted (assert the notification/signal call with employee/date/delta).
- **Open period:** no finalized run → applies normally, no signal, no block.
- **Audit identities:** after completion, requester (`RequestInstance`), approver (`RequestApproval`),
  and final approver (`CompletionRun.FinalApproverUserId`) are all resolvable for the corrected record.

Provisioning tests:
- 4→5 bump on a system-owned, un-customized type adds `checkIn`/`checkOut` fields, refreshes effect
  config, and seeds the 5 notification rules — idempotent on re-run.
- **Never overwrite customized:** a tenant-customized `ATTENDANCE_CORRECTION` form (extra/renamed
  fields) or a customized effect mapping / notification rule is left untouched by the bump.

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
