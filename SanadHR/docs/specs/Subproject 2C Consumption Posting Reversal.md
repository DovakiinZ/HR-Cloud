---
title: Subproject 2C Consumption Posting Reversal
aliases: [2C, Consumption Posting Reversal, 2B]
tags: [spec, payroll, finance]
---

# Sub-project 2C — Consumption, Posting & Reversal

> Source: `docs/superpowers/specs/2026-07-01-payroll-subproject-2c-consumption-posting-reversal-design.md`. **Shipped, merged via PR #11.** The genuine engine-consumption half (the original "2B").
> Up: [[Specs Index]] · Overview: [[Payroll Additions Deductions Overview]]

## Problem
Make 2A's records actually flow through a run — **consumed** as visible per-record payslip lines, **posted** one [[Immutable Ledger|ledger]] entry each on Execute, and **corrected via reversal** rather than reopening a closed period. Directly answers the user issue "the payroll needs to be edited even when it's approved."

## Design — three touch-points on the run lifecycle
- **Consume at Calculate** — `PayrollTransactionConsumer.GetConsumableAsync` selects `Approved` transactions in the run population whose **resolved target period** == the run period.
- **Post at Execute** — extend `PayslipLedgerMapper` + `PayrollItemExecutor`: one ledger entry per transaction, `Approved → Posted`, stamp metadata, idempotent.
- **Reverse on demand** — `IPayrollTransactionReversalService` → counter ledger entry via `IFinancialLedger.ReverseAsync` + `Posted → Reversed`, optional correction born `Draft` in the next open period.

## Key ADRs
- **[[ADR-Reversal-over-Reopen|Reversal instead of reopen]]** — runs stay immutable once Approved.
- **Resolve target period at run time** from `EffectiveDate` + cutoff, not the create-time stamp ([[DECISION_LOG|PAY-9]]).
- **No double-count** — `PayrollTransaction` and `EmployeeAdditions/Deductions` are **disjoint tables**; the `ADDITIONS`/`DEDUCTIONS` rules read only the recurring profile components, so consumed transactions are purely additive.
- **Attendance out of scope in 2C** (manual only) — sidesteps double-count; attendance handled in [[Subproject 2D Attendance Deduction Records|2D]].
- **No new migration**; ledger link reuses `ReferenceType`/`ReferenceId`.
- Business-rule failures throw **`DomainException` → 422** (Track-1 hotfix `0f7cd35`).

## Related
[[Immutable Ledger]] · [[Payroll Run State Machine]] · [[ADR-Reversal-over-Reopen]] · [[Subproject 2D Attendance Deduction Records]]
