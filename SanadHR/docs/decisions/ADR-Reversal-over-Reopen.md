---
title: ADR-Reversal-over-Reopen
aliases: [ADR PAY-4, Reversal over Reopen]
tags: [adr, payroll, finance]
status: accepted
---

# ADR PAY-4 — Reversal Model instead of Run-Reopen

> Up: [[DECISION_LOG]] · Related: [[Subproject 2C Consumption Posting Reversal]]

**Context.** Users asked to "edit an approved payroll." Reopening a closed period breaks auditability and immutability.

**Decision.** Runs stay **immutable once Approved**. Corrections are made by **reversing** a posted transaction (counter [[Immutable Ledger|ledger]] entry via `ReverseAsync`, `Posted → Reversed`) plus an optional correction born `Draft` in the next open period. **No reopen path is added.**

**Consequences.** Every correction is auditable and reproducible. Run-level equivalents (void / amend / reissue) extend this to the whole run — planned in [[Payroll Run Operations Roadmap|sub-project 6]].

## Related
[[Subproject 2C Consumption Posting Reversal]] · [[Immutable Ledger]] · [[Payroll Run State Machine]] · [[DECISION_LOG]]
