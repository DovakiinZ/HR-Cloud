# SP8 (+ F5) — Payroll Audit Surface — Design & Plan

> Part of the [final-phase master architecture](2026-07-04-payroll-final-phase-SP4-SP9-master-architecture.md).
> Principle: **no fourth audit system.** `AuditLog` (written by `IAuditLogService` from every payroll
> engine) already records every payroll action — creation, each state transition (`PayrollRun{State}`),
> void/amend/reissue, transaction actions — with actor, timestamp, and old/new JSON. SP8 is a unified,
> filterable READ over that existing data, plus surfacing the SP6 amendment chain.

## Backend
- `IPayrollAuditReadService.QueryAsync(PayrollAuditFilter, PagedRequest)` (Application) →
  `PagedResult<PayrollAuditRow>`. Reads `AuditLog` filtered to payroll `EntityType`s
  (`PayrollRun`/`PayrollTransaction`/`PayrollPayslip`), optional `runId` (EntityId), actor, action, date
  range; newest first. Resolves actor display names from `User.FullName`/`Email`.
- `PayrollAuditRow(Timestamp, Action, ActorUserId, ActorName, EntityType, EntityId, OldValues, NewValues)`.
- Endpoint `GET api/payroll/audit` (Payroll.Audit.View) — run-scoped (`?runId=`) or global.
- Amendment chain: extend the run summary read model with `AmendsRunId/AmendsRunNumber`,
  `SupersededByRunId/SupersededByRunNumber`, `VoidedAt`, `VoidReason` (columns exist from SP6 — no
  migration). One perm migration for `Payroll.Audit.View` (+ system-role grant).

## FE
- Run-page **audit** tab: paged, filterable table (action, actor, when, before/after) via the endpoint
  scoped to the run.
- Amendment-chain banner on the run page (supersedes / superseded-by links; void reason).
- (Global payroll audit page = fast-follow using the same endpoint without runId.)

## F5 (origin capture) — scoped
The actor + action + timestamp + old/new are already captured. The transaction `Origin` field already
exists (RunPage etc.). **Deferred as a noted enrichment:** IP + source-screen columns on `AuditLog`
(needs a schema change + middleware) and wiring `Origin=AttendanceDaily`. SP8 ships the read surface first.

## TDD task plan
1. `Payroll.Audit.View` perm (seed + Finance template + migration + grant) — perm-seed test.
2. `PayrollAuditReadService` + endpoint (integration; verified by build + live).
3. Amendment fields on the run summary + FE banner.
4. FE run-audit panel/tab.
