---
title: ADR-Immutable-Ledger
aliases: [ADR FIN-1, Immutable Ledger Decision]
tags: [adr, finance]
status: accepted
---

# ADR FIN-1 — Immutable Append-Only Ledger

> Up: [[DECISION_LOG]] · Related: [[Immutable Ledger]]

**Context.** Payroll money movement must be auditable and reproducible; editing financial rows destroys history.

**Decision.** All money movement is an **append-only `FinancialLedgerEntry`**. Entries are never updated or deleted. Corrections are **reversing entries** (`ReversesEntryId`, opposite direction) that net to zero. `Amount` rejects negatives — sign is semantic.

**Consequences.** Full audit trail; "edit an approved payroll" becomes a [[ADR-Reversal-over-Reopen|reversal]], not a mutation. Requires discipline everywhere finance is touched ([[CLAUDE|Definition of Done]]: never delete financial rows).

## Related
[[Immutable Ledger]] · [[Reproducibility]] · [[ADR-Reversal-over-Reopen]] · [[DECISION_LOG]]
