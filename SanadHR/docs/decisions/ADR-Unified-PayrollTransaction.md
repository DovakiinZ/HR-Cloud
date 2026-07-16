---
title: ADR-Unified-PayrollTransaction
aliases: [ADR PAY-3, Unified PayrollTransaction, Kind Discriminator]
tags: [adr, payroll, finance]
status: accepted
---

# ADR PAY-3 — Single PayrollTransaction + Kind Discriminator

> Up: [[DECISION_LOG]] · Related: [[Payroll Additions and Deductions]]

**Context.** Additions and deductions differ only by sign, which type catalog they reference, and their source. Two parallel entities would duplicate the lifecycle, state machine, and API.

**Decision.** One **`PayrollTransaction` with a `Kind` discriminator** (Addition/Deduction). `Amount` is non-negative; sign is implied by Kind (mirrors the [[Immutable Ledger|ledger]]). One state machine, one API filtered by kind, two pages over one store.

**Consequences.** It naturally becomes the `IPayrollTransaction` abstraction so new sources (attendance, loans, overtime) plug in without engine changes. Columns/states for later sub-projects were **defined up front** to avoid a second migration on the same table ([[DECISION_LOG|PAY-8]]).

## Related
[[Payroll Additions and Deductions]] · [[Subproject 2A Transaction Records]] · [[DECISION_LOG]]
