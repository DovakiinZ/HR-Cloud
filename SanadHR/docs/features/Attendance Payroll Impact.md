---
title: Attendance Payroll Impact
aliases: [Attendance Deduction Sync, Attendance Payroll Sync, Payroll Impact]
tags: [feature, attendance, payroll, finance]
---

# Attendance → Payroll Impact

> Attendance penalties and overtime become **visible [[Payroll Additions and Deductions|PayrollTransaction]] records** with a snapshot drill-down — the concrete realization of "no hidden deductions."
> Up: [[FEATURE_MAP]] · Modules: [[Attendance]] → [[Payroll Engine]]

## Mechanism
`IAttendancePayrollSyncService` (renamed from `AttendanceDeductionSyncService` in [[Subproject 2E Attendance Daily Overtime Excuse|2E]]) + the shared `AttendanceWageCalculator` turn per-period attendance aggregates into transactions:

- **Absence / Late / Shortage → Deduction** (2D).
- **Overtime → Addition** (2E, opt-in), consumed by the existing `ADDITIONS` rule.

Keyed on the **`AttendancePayrollKind` enum**, mapped to configurable types by code ([[ADR-Attendance-Penalty-Kind]]). Amounts use configurable rate multipliers (`CalcSettingsJson.attendanceRates`).

## Idempotent upsert
Per employee/period/kind: create (born `Approved`) if amount>0; update non-posted; **cancel-on-zero**; **skip Posted**. Snapshot breakdown stored in `PayrollTransactionAttendanceReference` (historically accurate).

## Triggers
- Guaranteed at **Calculate** (replaces the retired `ATTENDANCE_DED` rule).
- On-demand **Sync Now** (`POST /api/payroll/attendance-deductions/sync`).
- Per-employee **daily action** (`POST /api/attendance/payroll-impact/sync`, permission `Attendance.PayrollImpact.Create`) — 2E.

## Excuse handling
An approved excuse/leave zeroes penalty minutes at the source ([[Completion Effects Engine]]), so the next sync cancels the stale deduction before posting.

## Related
[[Attendance]] · [[Payroll Engine]] · [[Subproject 2D Attendance Deduction Records]] · [[Subproject 2E Attendance Daily Overtime Excuse]] · [[Payroll Additions and Deductions]]
