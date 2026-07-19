---
title: Subproject 2E Attendance Daily Overtime Excuse
aliases: [2E, Overtime Addition, Attendance Daily Actions]
tags: [spec, payroll, attendance, finance]
---

# Sub-project 2E — Daily Actions + Overtime + Rates + Excuse

> Source: `docs/superpowers/specs/2026-07-02-payroll-subproject-2e-attendance-daily-overtime-excuse-design.md`. **Built (7 TDD commits `c9468d5..c215de6`), not deployed.** The current "next" work.
> Up: [[Specs Index]] · Feature: [[Attendance Payroll Impact]]

## Four gaps it closes
1. **Overtime is unpaid** — no rule reads `OvertimeHours`.
2. Deduction amounts are implicitly `1.0×` with **no rate config**.
3. **No per-employee daily entry point** on the attendance page.
4. **Approved excuse/leave leaves penalty minutes stale** so cancel-on-zero never fires.

## Design
- **Rename + generalize** the 2D engine: `AttendanceDeductionSyncService` → **`AttendancePayrollSyncService`**; `AttendancePenaltyKind` → **`AttendancePayrollKind { Absence=1, Late=2, Shortage=3, Overtime=4 }`** — values 1–3 unchanged → **no data migration**.
- **Overtime → Addition** — `ResolveTypesAsync` returns per kind the type id + `PayrollTransactionKind`; Overtime → `AdditionType` code `OVERTIME` + `Kind=Addition`, consumed by the existing `ADDITIONS` rule. **No new rule, no double-count.** → [[DECISION_LOG|PAY-7]]
- **Configurable rates** — `CalcSettingsJson.attendanceRates` (defaults absence/late/shortage `1.0`, overtime `1.5`). Malformed/absent → defaults (never throws). Defaults reproduce 2D exactly.
- **Overtime is opt-in** (`includeOvertime` default false) — an existing tenant sees no change until it opts in.
- **Daily action** — `POST /api/attendance/payroll-impact/sync` (new permission `Attendance.PayrollImpact.Create`); materialized record stays the per-employee/period aggregate (per-day shape rejected in brainstorming).
- **Excuse/leave fix at the source** — `AttendanceCorrectionExecutor` zeroes late/shortage minutes; `AttendanceApplyLeaveDaysExecutor` upserts instead of blind-inserting a duplicate OnLeave row. Result: attendance shows no penalty → calculator aggregates zero → 2D's **cancel-on-zero** fires. No new cancellation code. → [[Completion Effects Engine]]

## Migration
One small **seed-data** migration — the new permission `Attendance.PayrollImpact.Create`. No table/column changes.

## Related
[[Attendance Payroll Impact]] · [[Attendance]] · [[Subproject 2D Attendance Deduction Records]] · [[Payroll Run Operations Roadmap]]
