---
title: Release Notes
aliases: [Releases, Shipments]
tags: [changelog]
---

# Release Notes

> Recent feature shipments, newest first. Detail lives in the [[Specs Index|specs]]; this is the timeline.
> Up: [[Changelog Index]]

## 2026-07-02 — Payroll Sub-project 2E (built, not deployed)
Attendance daily payroll-impact actions + overtime→Addition + configurable rate multipliers + excuse/leave cancel-on-zero fix. 7 TDD commits `c9468d5..c215de6`. New permission `Attendance.PayrollImpact.Create`. → [[Subproject 2E Attendance Daily Overtime Excuse]]

## 2026-07-02 — Payroll Sub-project 2D (shipped & deployed)
Attendance penalties → visible `PayrollTransaction` deduction records; `AttendancePenaltyKind`; `AttendanceWageCalculator`; `PayrollTransactionAttendanceReference` (first migration of the series); retired `ATTENDANCE_DED` rule. Finance 166/166. → [[Subproject 2D Attendance Deduction Records]]

## 2026-07-01 — Payroll Sub-project 2C (shipped, PR #11)
Transaction consumption + ledger posting + reversal model; resolved-target-period at run time; `DomainException`→422 hotfix. → [[Subproject 2C Consumption Posting Reversal]]

## 2026-06-30 — Payroll Sub-project 2A (shipped & deployed)
Unified `PayrollTransaction` + `Kind` discriminator, lifecycle state machine, `/payroll/additions` & `/deductions` pages. → [[Subproject 2A Transaction Records]]

## 2026-06-30 — Payroll Sub-project 1 (shipped & deployed)
Payroll types + selection scope + cutoff on versioned definitions; pluggable [[Scope Engine]]; 151/151 tests. → [[Payroll Types Scope Cutoff]]

## 2026-06-26 → 06-29 — Financial Calculation Engine (P1–P4)
Immutable ledger + rule/AST engine + dependency graph + versioned definitions + run state machine + batch execution + `PayrollController` + `/payroll` UI. EOS settlement UTC fix (`a8f5e85`). → [[Financial Calculation Engine]]

## Earlier
Access Management (deny-wins) · Termination/Restore approval · Document Platform · Dashboard Platform · Request Center refactor · Workflow Builder · Attendance/Leave engines. See [[Migration History]].

## Related
[[Migration History]] · [[Specs Index]] · [[IMPLEMENTATION_STATUS]]
