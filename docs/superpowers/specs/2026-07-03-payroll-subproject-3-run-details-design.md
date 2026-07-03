# Payroll Sub-project 3 — Run Details + Run-Page Quick Actions (+ close bug #4)

**Date:** 2026-07-03
**Status:** Design approved (brainstorm complete) — ready for implementation plan (writing-plans / TDD).
**Parent:** `2026-06-30-payroll-engine-redesign-master.md`
**Roadmap:** `2026-07-02-payroll-run-operations-enhancement-ROADMAP.md` — **Area 3** (+ Areas 7/8 folded in).
**Builds on (shipped):** 1 (types/scope/cutoff), 2A (transaction records), 2C (consume/post/reverse), 2D (attendance→deduction records). Assumes 2E deployed (overtime→addition, daily actions, `Attendance.PayrollImpact.Create`).

---

## 1. Goal

Build the **Payroll Run Details experience**: a scalable, deterministic, auditable run page with server-aggregated KPIs, a paginated employee table, an excluded-employees panel with reasons, a validation panel, a lifecycle + calculation timeline, and **run-page quick actions** to add additions/deductions (incl. attendance-type deduction and overtime-type addition presets) as `PayrollTransaction` records scoped to the run's context — while **fully closing orphan-transaction bug #4** and **preserving the immutable-ledger / read-only-consume / run-state-machine architecture**.

Every change is **additive**. No existing engine mechanics (ledger, consume-at-Calculate, run state machine, 2C/2D behaviour) are altered except where explicitly noted (the create-side guard routing and a relaxed `Recalculate`-from-later-states transition).

## 2. Scope

**In scope**
- Run-details **read model**: decomposed, paginated sub-resources with one consistent query contract; all KPIs server-side aggregates.
- **Excluded employees** with structural reasons (re-evaluated each Calculate); kept strictly separate from validation findings.
- **Validation findings** with three severities and rich, deep-linkable payloads.
- **Calculation history / versioning**: an append-only, monotonically-versioned calculation snapshot chain (metadata + findings + exclusions + change-summary).
- **Run-page quick actions**: one guarded create-from-run endpoint with typed presets; live transaction list; explicit Recalculate; inline Approve & Recalculate for privileged users.
- **Bug #4** closed on both doors (create-side guard + lifecycle staleness gate).
- New permission `Payroll.Transaction.CreateFromRun`; audit `Origin` + `CreatedFromRunId`.

**Out of scope (forward-compatible hooks only — NO placeholder UI)**
- **Payslips** (generate/print/download/store) → **SP4**.
- **Exports** (Excel/PDF/CSV/TXT, report types) → **SP5**.
- **Void / Amend / Reissue** → **SP6** (the structured `422 PAYROLL_PERIOD_CLOSED` payload is the hook).
- **Duplicate/conflict detection** → **SP9** (metadata exposed so no future schema change).
- **Attendance daily-calculation UI** → **2E** (already built).
- **Versioned payslip snapshots** → SP4/SP6 (SP3 keeps payslips current-only).

---

## 3. Design decisions (approved)

### D1 — Run period identity: stored typed columns; resolver is canonical
`PayrollRun` gains **`TargetPeriodYear`** and **`TargetPeriodMonth`** as typed, **indexed** columns, stamped at `CreateAsync` from the requested `(year, month)` and **immutable after creation**. They are the fast lookup for "find the payroll for this period."

**`PayrollPeriodResolver` remains the ONLY source of truth** for whether a transaction belongs to a run:
```
belongs(T, R) ⟺ employee(T) ∈ population(R, IsIncluded)
             ∧ PayrollPeriodResolver.Resolve(T.EffectiveDate, R.version.CutoffDay, R.version.CarryToNextPeriod)
               == (R.TargetPeriodYear, R.TargetPeriodMonth)
```
The transaction's own `TargetPeriodYear/Month` are **display-only** (naively stamped from `EffectiveDate` at create; never used for business logic). They may be retired in a future migration. Cutoff/carry-forward logic lives **only** in the resolver — never duplicated.

**Partial unique index** `(PayrollDefinitionId, TargetPeriodYear, TargetPeriodMonth)` filtered to **active** runs (`State <> Cancelled`; forward-compatible to also exclude SP6's `Voided`/`Superseded`), so two active runs cannot exist for the same payroll type + period, while SP6 amendments can coexist with a superseded original.

Every "find the payroll for this period" query uses the typed columns; **never** derive from `PeriodStart`/`PeriodEnd`.

### D2 — Bug #4 create-side guard: one place, becomes-Approved boundary, all paths
The guard lives **only in `PayrollTransactionService`**, at the moment a transaction **becomes `Approved`** (create-as-approved **and** the approve transition). **Every** path funnels through it — run page, Additions/Deductions pages, attendance sync, batch APIs, future imports. No controller or UI implements its own period validation.

- **Check:** for the transaction's employee + `EffectiveDate`, find any run `R` where `employee ∈ population(R)` ∧ resolver maps `EffectiveDate` (using **R's** pinned cutoff) to `R.(TargetPeriodYear, TargetPeriodMonth)` ∧ `PayrollRunStateMachine.IsImmutable(R.State)`. If found → **block**.
- **Blocks on** `IsImmutable` = `Approved | Executing | Completed | Locked | Archived`. **Allows** when no run exists or the run is still `Draft/Preview/Validated/PendingApproval` (those re-Calculate and consume normally).
- **Attendance sync** (2D/2E), which materialises born-`Approved` transactions directly, is refactored so its materialisation also passes through the shared guard. During `Calculate` the run is still mutable (passes); a Sync-Now/daily action against a closed period is correctly blocked.
- **Response:** `DomainException` → **HTTP 422**, error code **`PAYROLL_PERIOD_CLOSED`**, machine-readable payload:
  `{ ErrorCode, BlockingRunId, BlockingRunNumber, PayrollDefinitionId, TargetPeriodYear, TargetPeriodMonth, BlockingRunState }`.
  The same contract powers SP6's "Create Amendment" flow unchanged.

### D3 — Bug #4 lifecycle-side: staleness as a forward integrity gate
**Staleness is derived** (never a stored spontaneous state): a run is *stale* iff its current payslip snapshot ≠ its consumable set — i.e., an `Approved`/in-period/in-population transaction is **not** in the snapshot, **or** a snapshot `TXN:` line is now `Reversed`.

- While stale, **`Validate`, `SubmitForApproval`, and `Approve` are blocked** → `DomainException` → **422 `PAYROLL_RUN_STALE`** (payload includes the not-consumed count/ids). `Cancel` remains allowed.
- **`Recalculate` is the only operation that clears staleness.** It is allowed from any mutable state (`Draft/Preview/Validated/PendingApproval`) and **always resets the run to `Preview`**; the prior validation/submission becomes invalid → user must re-Validate → re-Submit → re-Approve.
- **No automatic state transitions, no silent mutations.** The run always represents exactly the numbers from its last successful `Calculate`.

Together D2 + D3 seal both orphan doors: D2 stops a transaction becoming `Approved` against an already-immutable run; D3 stops a run becoming immutable while consumable transactions are unreflected.

### D4 — Quick-action governance: PendingApproval + inline approve for privileged
Run-page manual transactions follow the **same governance as every other manual transaction**:
- Created as **`PendingApproval`** via the existing workflow (`SubmitImmediately=true`).
- If the actor holds **`Payroll.Approve`**, an inline **"Approve & Recalculate"** action approves the transaction and recalculates the run in one step; otherwise it waits for approval like the standalone pages.
- **All quick-add actions are hidden (not disabled)** once the run is immutable (`Approved+`).

### D5 — Live-sync & recalculation model
- The **transaction list is always live** (query over resolver-period + population; also picks up transactions created on the standalone pages → bidirectional sync).
- **Financial totals stay based on the last successful Calculate.** A prominent **"Recalculation Required"** banner shows whenever the *Approved-not-consumed* bucket is non-empty.
- **Recompute is explicit** (Recalculate = `CalculateAsync`; or inline Approve & Recalculate). **Never** auto-recompute per add; **never** silently update totals.
- **"Consumed in this run"** is **derived** from the current payslip `ComponentsJson` `TXN:` set — no new persistence.
- Transaction lifecycle buckets surfaced distinctly: **Pending Approval · Approved (not yet consumed) · Consumed in this run · Posted · Reversed**.
- **Calculation status badge:** `Up to date · Recalculation Required · Calculating · Failed`.

### D6 — Population validity model
- The **frozen population is the scope boundary** (never changes after creation; reproducible).
- **Validity is re-evaluated only during Calculate/Recalculate** and is part of that calculation's deterministic output; the run always represents its last successful calculation.
- **Excluded** (employee does not participate; no payslip) and **Validation Issue** (employee participates, but has warnings/errors) are **completely separate and never overlap**.
- **Structural exclusion reasons** (`PayrollExclusionReasonCode`): `ExcludedByScope`, `NotEmployedInPeriod` (hired after period end or terminated/left before it starts — not employed any day), `NoActiveSalary` (no wage to compute), `AlreadyInActiveRunForPeriod` (cross-run double-pay guard). Stable enum in `HR.Domain` (fixed meaning, localizable labels), not master data.
- **Validation findings** stay with the existing pluggable validators (see D7).

### D7 — Validation findings: severity + rich payload + pluggable
- **Three severities:** `Error`, `Warning`, `Information`. **Only `Error` blocks approval.** Warnings/Information never block.
- Every finding carries: **`Code`** (stable), **`Severity`**, human-readable **`Message`**, **`SuggestedAction`**, **`TargetModule/Screen`**, **`RelatedEntityType` + `RelatedEntityId`** (Employee/Attendance/Salary/PaymentMethod/…) — enabling FE deep-linking to the fix screen.
- Findings are **deterministic** (same data → same findings) and **pluggable** via the existing `IPayrollValidator` specification pattern (no hardcoding in the engine).
- `ValidateAsync`'s pass/fail becomes "**no `Error` findings**" (warnings/info do not block).

### D8 — Calculation snapshot & history (append-only, versioned)
Every Calculate/Recalculate creates a **new immutable `PayrollRunCalculation`**:
- **`CalculationVersion`** monotonic (1,2,3,…), **never reused**.
- Fields: `CalculatedAt`, `CalculatedByUserId`, `PayrollEngineVersion`, `PayrollDefinitionVersionId`, `EmployeeCount`, `IncludedEmployees`, `ExcludedEmployees`, `TransactionCountConsumed`, `ValidationSummary`, `FindingSummary`, `TotalsSnapshot` (gross/deduction/net), `Duration`, **`TriggerSource {Manual, Recalculate, Auto}`**, **`PreviousCalculationId`** (linked chain), **`ChangeSummary`** (lightweight human-readable deltas vs the previous version, e.g. "+12 transactions approved / −3 employees excluded / 2 warnings resolved").
- **Children:** `PayrollCalculationFinding` and `PayrollCalculationExclusion` rows tagged with the version.
- **Reproducible:** re-running version *N* against the same frozen data yields identical results.
- **Payslips stay current-only** for SP3 (versioned payslips are SP4/SP6).
- **Append-only:** never edit or delete calculation history; only append newer versions.
- **Extensible:** the model reserves attachment points so SP4 (payslip versions), SP6 (amendment refs) and ledger references can be added **without schema change** to the calculation chain.
- `PayrollRun` carries fast pointers: `CurrentCalculationVersion`, `LastCalculatedAt`, `LastCalculatedByUserId`.
- History APIs: `GET /runs/{id}/calculations`, `GET /runs/{id}/calculations/{version}` (independent of the summary endpoint).

### D9 — Read-model architecture: decomposed, paginated, aggregated
Decomposed sub-resources under one **consistent query contract** (pagination, sorting, filtering, searching) shared across the payroll module. Each endpoint returns **only its own responsibility** (no duplicated data), exposes **stable IDs over nested objects**, and computes **all KPIs as server-side aggregates**.

| Endpoint | Responsibility |
|---|---|
| `GET /runs/{id}` | **Summary only:** run metadata · KPI cards · calculation metadata · lifecycle timeline · health/recalculation status. **No employee/transaction/payslip rows.** |
| `GET /runs/{id}/employees` | Included employee payroll rows (paginated). Extensible for SP4 (payslip download, doc links) + SP6 (amendment history) without changing the summary. |
| `GET /runs/{id}/excluded` | Structural exclusions + reasons (paginated). Separate from validation. |
| `GET /runs/{id}/validation` | Validation findings (paginated). **Separate API from `/excluded`.** |
| `GET /runs/{id}/transactions` | Run-scoped transactions with the 5 buckets/status (paginated). **Single source of truth** for run transactions; extensible for attachments/audit/amendments. |
| `POST /runs/{id}/transactions` | Create-from-run (see D10). |
| `GET /runs/{id}/calculations`, `.../{version}` | Append-only calculation history. |

**KPI cards** (summary aggregates): Included employees · Excluded (→ panel) · Gross · Total Deductions · Net · Transactions consumed · Approved-not-consumed. Plus calculation-status badge + "last calculated by/at".

### D10 — Create-from-run action, permission, and Origin
- **One endpoint, typed presets** (never four): `POST /runs/{id}/transactions` with a single future-proof `CreatePayrollTransactionArgs`-style body allowing optional fields (no action-specific DTOs). Presets: *Add Deduction / Addition* (any type); *Add Attendance Deduction* = Deduction + ABSENCE/LATE/SHORTAGE; *Add Overtime Addition* = Addition + OVERTIME. **These are plain manual `PayrollTransaction`s** (`SourceModule=Manual`) — they do **not** trigger attendance calculation (that is the 2E daily screen).
- **Auto-inherited from run context** (user never chooses): `PayrollDefinitionId`, `TargetPeriodYear`, `TargetPeriodMonth`, `EmployeeId`. **`EffectiveDate` defaults to a canonical date inside the run period** (e.g. `PeriodEnd`) so the resolver deterministically maps the transaction to this run; if a caller supplies an `EffectiveDate` it **must resolve (via the run's pinned cutoff) to the run's target period**, else `422` — a create-from-run transaction can never silently land in a different period. The transaction's display `TargetPeriodYear/Month` are set to the run's (still display-only; membership is always resolver-verified per D1).
- **Governance:** funnels through the same guarded `PayrollTransactionService.CreateAsync` → `PendingApproval` (D4) → becomes-Approved guard (D2).
- **Permission:** new **`Payroll.Transaction.CreateFromRun`**, seeded in `SeedData` and granted to system roles via a **grant migration** (same pattern as `Attendance.PayrollImpact.Create`). Reads use `Payroll.View`, approve uses `Payroll.Approve`, recalculate uses `Payroll.Run`.
- **Audit / provenance:**
  - **`Origin`** (`PayrollTransactionOrigin`, **non-nullable**) — the UI/API screen: `System, RunPage, AttendanceDaily, DeductionsPage, AdditionsPage, Import, API, Migration, Workflow, ESS, Scheduler` (extra values reserved now). Run-page creates → `RunPage`; existing rows + engine/attendance-sync backfill → `System` (2E daily action → `AttendanceDaily`).
  - **`SourceModule`** (business system) and **`Origin`** (UI/API) are **separate concerns** and never replace each other.
  - **`CreatedFromRunId`** (permanent) — *where the transaction was created*; distinct from **`PayrollRunId`** (*which run eventually consumed/posted it*). Audit exposes **`CreatedFromRunNumber`** so support can say "created from Payroll Run PR-2026-07".
  - Duplicate detection is **deferred to SP9**, but `Origin` + `CreatedFromRunId` + `SourceModule` + `ReferenceType/ReferenceId` are exposed so SP9 needs no schema change.

---

## 4. Data model & migrations

**Enums (`HR.Domain/Enums`)**
- `PayrollExclusionReasonCode { ExcludedByScope, NotEmployedInPeriod, NoActiveSalary, AlreadyInActiveRunForPeriod }`
- `PayrollTransactionOrigin { System=0, RunPage, AttendanceDaily, DeductionsPage, AdditionsPage, Import, API, Migration, Workflow, ESS, Scheduler }`
- `PayrollCalculationTriggerSource { Manual, Recalculate, Auto }`
- `ValidationSeverity { Error, Warning, Information }` (extend/confirm existing finding severity; ensure `Information` present and only `Error` blocks).

**Entities**
- `PayrollRun` (+ `TargetPeriodYear:int`, `TargetPeriodMonth:int`, `CurrentCalculationVersion:int`, `LastCalculatedAt:DateTime?`, `LastCalculatedByUserId:Guid?`).
- `PayrollRunCalculation` (new) + `PayrollCalculationFinding` (new) + `PayrollCalculationExclusion` (new) — append-only, versioned, chained.
- `PayrollTransaction` (+ `Origin:PayrollTransactionOrigin` non-nullable, `CreatedFromRunId:Guid?`).
- `PayrollRunPopulation` unchanged (frozen scope boundary).

**Migrations (chronological, additive)**
1. `PayrollRunTargetPeriodAndCalcPointers` — add run period columns + calc pointers; **backfill** `TargetPeriodYear/Month` from `PeriodStart`; add the filtered unique index.
2. `PayrollRunCalculationHistory` — `PayrollRunCalculation` + findings + exclusions tables.
3. `PayrollTransactionOriginAndCreatedFromRun` — add `Origin` (backfill `System`) + `CreatedFromRunId`.
4. `PayrollCreateFromRunPermission` — seed `Payroll.Transaction.CreateFromRun`.
5. `GrantPayrollCreateFromRunToSystemRoles` — idempotent grant to system roles (mirrors `GrantAttendancePayrollImpactToSystemRoles`).

(Adjacent migrations may be combined during implementation; keep the permission seed + grant as the established two-step.)

---

## 5. Backend components

- **`PayrollRunEngine`** — add `RecalculateAsync` semantics (allow from any mutable state → `Preview`); write a `PayrollRunCalculation` snapshot each Calculate (metadata, findings, exclusions, change-summary, previous link); compute validity exclusions; compute consumed count; refresh run pointers; enforce the **staleness gate** on `Validate/Submit/Approve`.
- **Staleness evaluator** — pure, derived comparison of current snapshot vs consumable set (reused by the gate and the summary badge). Cutoff via resolver only.
- **`PayrollTransactionService`** — the **single** guard site at becomes-Approved (D2); create-from-run inheritance + `Origin`/`CreatedFromRunId` stamping; structured `PAYROLL_PERIOD_CLOSED`.
- **Attendance sync** (`AttendancePayrollSyncService`) — route materialisation through the shared guard.
- **Read services** — server-side aggregate queries for summary KPIs; paginated employees/excluded/validation/transactions/calculations with the shared query contract.
- **`PayrollController`** — evolve `GET runs/{id}` to summary-only; add the sub-resource endpoints + `POST runs/{id}/transactions`; permission attributes per D10.
- **Change-summary builder** — derive lightweight deltas between consecutive `PayrollRunCalculation` versions (transaction/exclusion/finding counts, trigger); richer semantic deltas (e.g. "salary updated for N") best-effort/extensible.

## 6. Frontend

`/payroll/runs/[id]` reorganised (RTL, existing design system):
- Header: run number · state badge · **calculation-status badge** · period · last-calculated by/at.
- **KPI cards** (summary aggregates only).
- **"Recalculation Required" banner** when Approved-not-consumed is non-empty; Recalculate + (privileged) Approve & Recalculate.
- Panels: **Employees** (paginated) · **Excluded** (reasons + deep-link) · **Transactions** (5 buckets, quick-add presets, inline approve) · **Validation** (severity + suggested action + deep-link) · **Timeline** (lifecycle transitions + calculation history).
- Quick-add presets **hidden** when immutable. Clean slots reserved for SP4 (payslip download) / SP5 (export) / SP6 (void-amend) — **no placeholder buttons**.
- API layer (`src/lib/api/payroll*.ts`) extended with the decomposed endpoints + shared paginated query contract; `usePermission`/`AccessGuard` gating.

## 7. Testing (TDD)

Primary: `HR.Domain.Finance.Tests` (+ module/controller tests). Cover:
- **Bug #4 door 1:** guard blocks becomes-Approved against every immutable state, from every path; allows on mutable/no-run; correct `PAYROLL_PERIOD_CLOSED` payload; attendance-sync routing.
- **Bug #4 door 2:** staleness derivation (approved-not-consumed / reversed-consumed); gate blocks `Validate/Submit/Approve`; `Recalculate` clears + resets to `Preview`; no spontaneous transitions.
- **Period identity:** typed columns stamped + immutable; filtered unique constraint; membership via resolver (not naive columns); cutoff/carry cases.
- **Calculation history:** monotonic non-reused versions; append-only; previous-link chain; snapshot fields; change-summary deltas; reproducibility; payslips current-only.
- **Validity vs validation:** structural exclusions computed at Calculate; excluded ∩ findings = ∅; three severities; only `Error` blocks; rich finding payload.
- **Create-from-run:** inheritance (definition/period/employee), `EffectiveDate` defaulting to run period, `Origin=RunPage`, `CreatedFromRunId`, `SourceModule=Manual`; permission gating; presets.
- **Read endpoints:** pagination/sort/filter/search contract; server-side aggregates; responsibility separation; stable IDs.

## 8. Immutability & compatibility

- Consume stays **read-only** (no `PayrollRunId` stamping at Calculate); ledger untouched; posting/stamping still at Execute.
- Run state machine + `IsImmutable` semantics preserved; the only lifecycle change is `Recalculate` allowed from later mutable states resetting to `Preview` (deliberate, D3).
- 2C/2D mechanics unchanged except additive columns + guard routing.
- All new provenance/history is append-only, matching the project's audit ethos.

## 9. Deferred / follow-ups (recorded, not built)

- SP4 payslips · SP5 exports · SP6 void/amend/reissue (consumes the `PAYROLL_PERIOD_CLOSED` hook) · SP9 duplicate detection (consumes `Origin`/`CreatedFromRunId`/`SourceModule`/`ReferenceType/Id`).
- Retiring the transaction's naive display `TargetPeriodYear/Month` (future migration).
- Richer semantic change-summary deltas (input-diff based).

## 10. Permissions summary

New: **`Payroll.Transaction.CreateFromRun`** (seeded + granted). Reuse: `Payroll.View` (reads), `Payroll.Approve` (inline approve), `Payroll.Run` (calculate/recalculate), `Payroll.Lock` (execute — unchanged). All via the existing deny-wins Access Management + `usePermission`/`AccessGuard`.
