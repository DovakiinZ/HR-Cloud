---
title: Subproject 2A Transaction Records
aliases: [2A, Transaction Records]
tags: [spec, payroll, finance]
---

# Sub-project 2A — Transaction Records, Lifecycle & Pages

> Source: `docs/superpowers/specs/2026-06-30-payroll-subproject-2a-transaction-records-design.md`. **Shipped & deployed.**
> Up: [[Specs Index]] · Overview: [[Payroll Additions Deductions Overview]]

## Problem
Deliver the record store, its lifecycle, and the HR/Finance pages so additions/deductions are visible *before* the engine reads them. **No engine consumption yet** (that's [[Subproject 2C Consumption Posting Reversal|2C]]).

## Key decision — unified entity
A **single `PayrollTransaction` with a `Kind` discriminator** (Addition/Deduction), not two parallel entities — identical shape, one lifecycle, one state machine, one API. It naturally *becomes* the `IPayrollTransaction` abstraction later. → [[ADR-Unified-PayrollTransaction]]

## Data model
`PayrollTransaction : TenantEntity` (table `engine_payroll_transactions`). Enums `PayrollTransactionKind`, `PayrollTransactionStatus {Draft, PendingApproval, Approved, Rejected, Cancelled, CarriedForward, Posted, Reversed}`. `Amount` decimal(18,2) **non-negative** (sign implied by Kind, mirrors the [[Immutable Ledger|ledger]]). Separate `TransactionDate`/`EffectiveDate` (business calc uses `EffectiveDate`). Posting-metadata columns (`PayrollRunId`/`PostedAt`/`PostedBy`/`LedgerEntryId`/`ReversesTransactionId`) **defined now but inert**. `PayrollTransactionStateMachine` mirrors the run machine; `IsImmutable` once Posted.

## Key decision — define-now / populate-later
Columns and states that 2C/2D depend on are defined here **so no second migration churns the same table** ([[DECISION_LOG|PAY-8]]). Editable/deletable only in `Draft`.

## Status
✅ Shipped & deployed. Migration `PayrollTransactions` (new table + indexes only). Pages `/payroll/additions`, `/payroll/deductions`.

## Related
[[Payroll Additions and Deductions]] · [[Subproject 2C Consumption Posting Reversal]] · [[ADR-Unified-PayrollTransaction]]
