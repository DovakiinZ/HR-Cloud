---
title: Subproject 2D Attendance Deduction Records
aliases: [2D, Attendance Deduction Records]
tags: [spec, payroll, attendance, finance]
---

# Sub-project 2D — Attendance → Deduction Records

> Source: `docs/superpowers/specs/2026-07-02-payroll-subproject-2d-attendance-deduction-records-design.md`. **Shipped & deployed (2026-07-02).**
> Up: [[Specs Index]] · Feature: [[Attendance Payroll Impact]]

## Problem
Deliver "no hidden deductions": attendance penalties (absence/late/shortage) become **visible, traceable [[Payroll Additions and Deductions|PayrollTransaction]] deduction records** in `/payroll/deductions` before approval, each drilling down to the exact attendance records, flowing through [[Subproject 2C Consumption Posting Reversal|2C]]'s consume→post→reverse spine. **Retires the seeded `ATTENDANCE_DED` rule.**

## Design
- **`AttendancePenaltyKind { Absence, Late, Shortage }`** — engine keys on this **enum, not master-data labels**; each maps to a configurable `DeductionType` **by code** (`ABSENCE`/`LATE`/`SHORTAGE`). → [[ADR-Attendance-Penalty-Kind]]
- **`AttendanceWageCalculator`** — extracted shared wage math so the fact provider and the sync service use **one formula** (no drift).
- **`AttendanceDeductionSyncService.SyncAsync`** — per employee/period/kind **idempotent upsert**: create (born `Approved`) if amount>0; update non-posted; **cancel-on-zero**; **skip Posted**.
- **`PayrollTransactionAttendanceReference`** — dedicated drill-down table with **snapshot columns** (the series' **first migration**), historically accurate even if attendance later changes.

## Triggers (A+C hybrid)
Guaranteed materialization at **Calculate** (replaces the always-on rule) + on-demand **Sync Now** `POST /api/payroll/attendance-deductions/sync`.

## Rule retirement
Remove `ATTENDANCE_DED` from the seeder for new tenants; **deactivate (not delete)** on existing tenants — preserves historical auditability. Seed `LATE` + `SHORTAGE` deduction types.

## Related
[[Attendance Payroll Impact]] · [[Attendance]] · [[ADR-Attendance-Penalty-Kind]] · [[Subproject 2E Attendance Daily Overtime Excuse]]
