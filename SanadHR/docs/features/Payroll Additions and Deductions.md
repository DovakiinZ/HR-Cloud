---
title: Payroll Additions and Deductions
aliases: [Additions and Deductions, PayrollTransaction, Additions Deductions]
tags: [feature, payroll, finance]
---

# Payroll Additions & Deductions

> Every addition/deduction is a **visible, traceable `PayrollTransaction` record** that exists before approval. "No hidden deductions."
> Up: [[FEATURE_MAP]] · Module: [[Payroll Engine]] · Spec series: [[Payroll Additions Deductions Overview]]

## What it is
A single `PayrollTransaction` entity with a **`Kind` discriminator** (Addition/Deduction) — one lifecycle, one state machine, one API, two filtered pages (`/payroll/additions`, `/payroll/deductions`). It becomes the `IPayrollTransaction` abstraction, so new sources (manual, attendance, loans, overtime) plug in without touching the engine.

## Lifecycle
`Draft → PendingApproval → Approved → (Consumed at Calculate) → Posted (at Execute) → Reversed`. Once **Posted** it is immutable — corrections happen via [[ADR-Reversal-over-Reopen|reversal]], writing a counter [[Immutable Ledger|ledger]] entry.

## How it flows through a run
1. **Consume at Calculate** — approved transactions whose resolved target period matches become per-record payslip lines ([[Subproject 2C Consumption Posting Reversal]]).
2. **Post at Execute** — one ledger entry per transaction, metadata stamped.
3. **Reverse on demand** — counter-entry + optional correction in the next open period.

## Sources
- **Manual** (2A/2C) — HR-entered.
- **Attendance** — penalties→deductions, overtime→additions ([[Attendance Payroll Impact]]).
- Future: loans, expenses, court orders, commissions, bonuses.

## Related
[[Payroll Engine]] · [[Immutable Ledger]] · [[Payroll Run State Machine]] · [[Attendance Payroll Impact]] · [[Subproject 2A Transaction Records]] · [[ADR-Unified-PayrollTransaction]]
