---
title: Payroll Run State Machine
aliases: [Run State Machine, PayrollRun States, Run Lifecycle]
tags: [diagram, finance, state-machine]
---

# Payroll Run State Machine

> The lifecycle of a `PayrollRun`. `PayrollRunStateMachine` enforces transitions; invalid ones throw `InvalidStateTransitionException`. States expose `IsImmutable` / `IsTerminal`.
> Up: [[Diagrams Index]] · Engine: [[Financial Calculation Engine]] · Decision: [[DECISION_LOG|FIN-5]]

```
        ┌─────────┐
        │  Draft  │
        └────┬────┘
             │ Calculate (runs attendance sync + fact build + rule eval)
        ┌────▼────┐
        │ Preview │   (spec's "Calculated" == engine's Preview)
        └────┬────┘
             │ Validate
        ┌────▼──────┐
        │ Validated │
        └────┬──────┘
             │ Submit
     ┌───────▼────────┐
     │ PendingApproval│
     └───────┬────────┘
             │ Approve
        ┌────▼─────┐
        │ Approved │◄── immutable from here (correct via reversal, not reopen)
        └────┬─────┘
             │ Execute (batch post → ledger, idempotent/resumable)
        ┌────▼──────┐
        │ Executing │──► Failed (retriable)
        └────┬──────┘
             │
        ┌────▼──────┐
        │ Completed │──► Locked ──► Archived
        └───────────┘

  Cancelled: reachable from pre-Approved states.
```

- **Calculate** = attendance→deduction [[Attendance Payroll Impact|sync]] + [[Financial Calculation Engine|fact build]] + dependency-ordered [[Rule Engine|rule evaluation]] → frozen [[Snapshot and Versioning|payslip components]].
- **Execute** = post components to the [[Immutable Ledger]], one entry per component/transaction.
- Once **Approved**, the run is immutable — edits happen via [[Subproject 2C Consumption Posting Reversal|reversal]] ([[ADR-Reversal-over-Reopen]]). Run-level void/amend/reissue is planned ([[Payroll Run Operations Roadmap]]).

Sibling: `PayrollTransactionStateMachine` (Draft/PendingApproval/Approved/Rejected/Cancelled/CarriedForward/Posted/Reversed) — see [[Payroll Additions and Deductions]].

## Related
[[Financial Calculation Engine]] · [[Payroll Lifecycle]] · [[Payroll Engine]] · [[Test Suite]]
