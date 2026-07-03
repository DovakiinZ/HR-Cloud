# Payroll Engine — Final-Phase Master Architecture (SP4 → SP9)

> Status: **APPROVED 2026-07-04** by the product owner. This is the single source of truth for the
> remaining Payroll Engine build-out. Each sub-project runs its own brainstorm→spec→plan→TDD→review→
> deploy cycle, but must conform to the decisions recorded here. Reconciled against `main` (HEAD
> `8df0dce`) — the vault ROADMAP/IMPLEMENTATION_STATUS lag one shipment behind; trust this doc + the
> memory dir.

## Current state (verified)

- Shipped + deployed: Financial Engine P1–P4, SP1 (types/scope/cutoff), 2A, 2C, 2D, **SP3** (run
  details, quick-add, **bug #4 closed** via `PayrollPeriodGuard` + derived staleness gate). 250/250
  finance tests.
- **2E is MERGED to main** (commits `dc6a690`..`c215de6`) but **NOT deployed** — needs the
  `AttendancePayrollImpactPermission` migration applied to Azure + API redeploy only.
- **Blocker for SP6:** the `{success,data,message,errors}` envelope (`src/lib/api-client.ts`) has no
  machine `code`; FE regexes `message` to detect `PAYROLL_PERIOD_CLOSED`/`PAYROLL_RUN_STALE`.

## Non-negotiable principles (hold across every SP)

1. **Reuse existing engines — no parallel implementations.** Payslips render through the existing
   QuestPDF `DocumentRenderer` + `DocumentTemplate` block model + `CompanyBranding`. Validation
   extends the DI-discovered `IPayrollValidator` framework. Exports use an exporter registry.
   Corrections obey **ADR PAY-4 (Reversal-over-Reopen)**. Audit consolidates on `AuditEntry` +
   `TimelineEvent` + `PayrollRunTransition` — no third/fourth audit system.
2. Immutability & reproducibility of the ledger and payslip snapshots is sacred (append-only,
   decimal(18,2), pinned definition/rule-set/template versions).
3. DDD + SOLID + Clean Architecture layering (`HR.Domain.Engines.Finance` / `HR.Application` /
   `HR.Infrastructure` / `HR.Modules.Payroll`). Multi-tenant (`TenantEntity`).
4. Minimal future migrations — build cross-cutting foundations first.

## Cross-cutting foundations (build FIRST)

- **F1 — Structured error contract.** Additive `code` on the ApiResponse envelope + exception
  middleware mapping (DomainException/PayrollPeriodClosedException/staleness → code). FE `ApiError`
  gains `.code`; stop regexing messages. No DB migration. Unblocks SP6.
- **F2 — Payslip document data-provider.** Maps `PayrollPayslip.ComponentsJson`/`FactsJson` +
  employee identity + `CompanyBranding` → the token/block model `DocumentRenderer` consumes. Spine
  for SP4 render + SP5 PDF.
- **F3 — Payroll export framework.** `IPayrollExportHandler` strategy + DI registry +
  `PayrollExportJob` (Hangfire-ready, reuse execution-scheduler pattern).
- **F4 — Unified generated-artifact store.** Reuse `GeneratedDocument` + DB-backed file store as the
  immutable, versioned home for payslip PDFs and export files, linked to run/employee.
- **F5 — Audit/origin capture context.** `IPayrollAuditContext` via middleware (user/IP/
  `X-Origin-Screen`/route); wires the present-but-unused `Origin=AttendanceDaily`.

## SP4 — Dynamic Payslip Template Engine (owner-amended)

**The payslip is NOT a hardcoded PDF. It is a new template TYPE inside the existing Document Template
engine.** One data model → many renderers (PDF / Print / Email / ESS / Archive).

- Reuse `PayrollPayslip` (immutable snapshot) as the data model. Reuse `DocumentTemplate` /
  `DocumentTemplateVersion` / `DocumentRenderer` / `DocumentTokenResolver` / `CompanyBranding` and the
  Document Template Builder UI (`DocumentDesigner`).
- Add a `Payslip` template type. `{{Payroll.Components}}` is a **repeating block that loops over
  `ComponentsJson`** — any future allowance/deduction (Remote Work, Risk, Night Shift, Fuel…) appears
  automatically, zero code. GOSI renders as just another deduction component now; a full statutory
  GOSI calc-engine is a later dedicated SP.
- Ship one **enterprise bilingual (AR/EN) default template**: branded header (logo, AR/EN name, CR,
  VAT, address, phone, email, website), employee block (name, number, national id, dept, position,
  IBAN, payment method, period, pay date), earnings/deductions/totals, footer (QR, stamp, signature,
  generated date/by, run number, payroll version, reproducibility line).
- Every block add/remove/hide/reorder/rename/style-able via the builder; bilingual labels; rich text;
  images; theme colours; fonts; margins; header/footer.
- Tokens: `{{Company.*}}`, `{{Employee.*}}`, `{{Payroll.Period|PayDate|RunNumber|NetSalary|
  GrossSalary|TotalDeductions|TotalEarnings|Components|QR|GeneratedAt|GeneratedBy}}`.
- **Template versioning:** each run pins the payslip template version used → historical payslips never
  change when the template is later edited.
- Generation: render-on-demand + cache, plus bulk archive-on-Approve (background job → F4 store).
- Endpoints (extend `api/payroll`): `GET runs/{id}/payslips`, `GET runs/{id}/payslips/{empId}`,
  `GET .../pdf?print=`, `POST runs/{id}/payslips/generate`, `GET employees/{id}/payslips`,
  `POST .../email`.
- FE: reuse `request-center.ts` view/download/print/email pattern (fix its `emailRequestDocument`
  backslash bug) → payslip tab on run page + employee profile + ESS self-view.
- Perms: `Payroll.Payslip.View/Print/Download` + employee-self resource rule.

## SP5 — Pluggable bank-export + reports framework (owner-amended)

**Country-agnostic. Nothing hardcoded.** Pipeline: `Bank Profile → Field Mapper → Validator →
Exporter → Generated File`. Validation is separated from file generation. Export templates are
versioned. Saudi WPS/SIF is the **first registered profile**, not a special case. Ready for future
custom bank profiles + CSV/TXT/XML/fixed-width/API-based exporters without engine changes.

- `IPayrollExportHandler` registry (F3). Report types: RunSummary, EmployeeDetail, Additions,
  Deductions, AttendanceImpact, Excluded, Payslips(bulk zip), BankTransfer/WPS-SIF.
- Formats: Excel (ClosedXML, reuse employee-export pattern), PDF (F2), CSV, TXT, XML, fixed-width.
- `PayrollExportJob` entity (run-scoped, format, reportType, status, artifactId, requestedBy, params);
  async via existing scheduler or sync for small.
- Bank profile = plugin: profile definition + configurable field mapping + separate validator +
  exporter. Gated by finer `Payroll.Export.Bank`.
- Endpoints: `POST runs/{id}/exports`, `GET runs/{id}/exports`, `GET exports/{jobId}`,
  `GET exports/{jobId}/download`.
- Perms: `Payroll.Export`, `Payroll.Export.Bank`.

## SP6 — Void / Amend / Reissue + versioning (heaviest; needs F1)

- **Void** (Completed/Locked): `IFinancialLedger.ReverseAsync` all run entries + flip consumed txns
  `Posted→Reversed` → new terminal `PayrollRunState.Voided` (append-only enum value) + audit.
- **Amend:** new `PayrollRun` with `AmendsRunId`→old, `SupersededByRunId` on old; **full recalc of the
  affected population but DELTA ledger posting** (post only net diff vs superseded run —
  `LedgerDeltaCalculator`, reuse `PayslipLedgerMapper`). Old run stays immutable.
- **Reissue:** regenerate payslips (SP4) with a version chain superseding old payslip docs.
- `AmendsRunId`/`SupersededByRunId` linked list = amendment chain + run versioning.
- Perms: `Payroll.Run.Void/Amend/Reissue`. Migration: run link columns + `Voided` enum + void meta.

## SP7 — Permissions/RBAC (folded + final consolidation pass)

Register all new perms in `SeedData` + `AccessTemplateSeeder` with grant migrations; deny-wins
resolver unchanged. `Payroll.Payslip.*`, `Payroll.Export.Bank`, `Payroll.Run.Void/Amend/Reissue`,
`Payroll.Audit.View`, ESS self rule. No parallel RBAC.

## SP8 — Audit surface (on F5, after SP6)

Unified read API `GET api/payroll/audit` (filter run/employee/actor/action/origin/date) over
`AuditEntry`+`TimelineEvent`+`PayrollRunTransition`+`PayrollRunCalculation`; origin/screen capture;
who-voided/reissued + old↔new linkage from SP6. FE audit timeline + global payroll audit log.
Perm `Payroll.Audit.View`.

## SP9 — Duplicate / conflict / consistency validation

Extend `IPayrollValidator`: `DuplicateTransactionValidator` (employee+type+period+amount window),
`ConflictValidator` (deduction>gross, negative net, overlaps), `ConsistencyValidator` (missing
structure, unmapped component). Soft-warn at create-time; full findings at calculate/validate (reuse
3-severity run-validation panel). Absorbs deferred `IPayrollTransaction` priority/ordering (§11/§12).
Thresholds in `CalcSettingsJson` (likely no migration).

## Sequenced plan

`F1` → **deploy 2E** → `F2+F4` → **SP4** → `F3` → **SP5** → `F5` → **SP6** → **SP8** → **SP9** →
**SP7 consolidation**. Each SP: full TDD, review, fix, merge to main, **auto-deploy to Azure**
(standing authorization), then sync Obsidian + CLAUDE.md + ROADMAP + PROJECT_STATUS + changelog.

## Owner decisions (2026-07-04)

- **Bank/WPS:** generic pluggable framework now; Saudi WPS/SIF = first profile; no hardcoded banks.
- **GOSI:** renders as a normal component on the payslip now; full statutory GOSI pack = later SP.
- **Payslips:** dynamic template engine (above), not a static PDF.
- **Deploy:** auto-deploy each SP as it merges (standing authorization). Deploy 2E first.
