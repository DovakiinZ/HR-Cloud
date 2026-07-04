# SP6 — Void / Amend / Reissue + Run Versioning — Design & Plan

> Part of the [final-phase master architecture](2026-07-04-payroll-final-phase-SP4-SP9-master-architecture.md).
> Basis: **ADR PAY-4 (Reversal-over-Reopen)** — an Approved+ run is never reopened; corrections are ledger
> reversals + linked runs. Unblocked by F1 (structured error codes).

## Key simplification
The ledger is append-only and nets correctly, so **Amend = Void the old run + create a linked fresh run**
(−old + new = correct net). This avoids per-employee delta-posting math while staying correct, immutable,
and auditable. It reuses the Void mechanics + the normal run lifecycle. (Delta-posting — reposting only
changed employees — is a later optimization; the amendment chain is unchanged by it.)

## Model changes
- `PayrollRun`: `AmendsRunId` (→ the run this amends), `SupersededByRunId` (on the old run), `VoidedByUserId`,
  `VoidedAt`, `VoidReason`. The `AmendsRunId`/`SupersededByRunId` linked list **is** the amendment chain /
  run versioning.
- `PayrollRunState`: new terminal `Voided` value (append-only enum). State machine: `Completed → Voided`
  and `Locked → Voided`; `Voided` terminal. **`IsImmutable` excludes `Voided`** — a voided run releases
  its hold on the period so the amend run can own it (the PayrollPeriodGuard already keys off IsImmutable).

## Services
**Void** (`IPayrollRunAmendmentService.VoidAsync(runId, reason)`):
1. Require state Completed/Locked (posted). `EnsureCanTransition(state, Voided)`.
2. `_ledger.QueryAsync(new LedgerQuery { PayrollRunId })` → for every entry with `Status == Posted`,
   `_ledger.ReverseAsync(entry.Id, reason)` (append counter-entries; originals untouched).
3. Flip every consumed `PayrollTransaction` (PayrollRunId == runId, Status == Posted) to `Reversed`
   (their ledger entries were already reversed in step 2 — no double reverse).
4. Transition to `Voided`; stamp VoidedBy/At/Reason; write `PayrollRunTransition` + `IAuditLogService`.

**Amend** (`AmendAsync(oldRunId, reason)`):
1. Require old run posted (Completed/Locked). Void it (above).
2. Create a NEW `PayrollRun` cloning the pinned identity (PayrollDefinitionVersionId, RuleSetVersionId,
   period, TargetPeriodYear/Month), `AmendsRunId = oldRunId`, `State = Draft`, fresh RunNumber; freeze its
   population via `IScopeEngine` (as `CreateAsync` does). Set `old.SupersededByRunId = new.Id`.
3. The new run then follows the normal lifecycle (edit txns → calculate → validate → approve → execute),
   posting fresh amounts. Net ledger = −old + new = the correction.

**Reissue** (`ReissueAsync(runId)`): regenerate + re-archive the run's payslips — reuses SP4
`IPayslipDocumentService.ArchiveRunAsync`. Gated by its own permission for audit of who re-issued.

## Endpoints (`api/payroll`)
- `POST runs/{id}/void` (Payroll.Run.Void) — body `{ reason }`.
- `POST runs/{id}/amend` (Payroll.Run.Amend) — body `{ reason }` → returns the new run id.
- `POST runs/{id}/reissue` (Payroll.Run.Reissue).

## Permissions
`Payroll.Run.Void`, `Payroll.Run.Amend`, `Payroll.Run.Reissue`. Seed + grant migration (Finance gets all;
Payroll Officer gets Reissue). Deny-wins resolver unchanged.

## FE
Run-page header actions (shown only for Completed/Locked, gated by perm): Void (confirm + reason), Amend
(confirm + reason → navigate to the new run), Reissue (confirm). Show the amendment chain in the timeline
(supersedes / amends links). Uses F1 `err.code` for structured errors.

## TDD task plan
1. State machine: add `Voided` (Completed/Locked→Voided, terminal, IsImmutable excludes it) — pure, RED→GREEN.
2. Entity columns + `Voided` enum + migration + 3 perms + grant.
3. `PayrollRunAmendmentService.VoidAsync` — reverse ledger + flip txns + transition (integration).
4. `AmendAsync` — void old + create linked run (integration).
5. `ReissueAsync` — reuse SP4 archive.
6. Endpoints + DTOs + perms.
7. FE actions + amendment-chain display.

## Migration footprint
One migration: PayrollRun columns (AmendsRunId, SupersededByRunId, VoidedByUserId, VoidedAt, VoidReason) +
3 permission rows + system-role grant.
