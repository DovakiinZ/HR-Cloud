---
title: ADR-Attendance-Penalty-Kind
aliases: [ADR PAY-6, Attendance Penalty Kind, AttendancePayrollKind]
tags: [adr, payroll, attendance, finance]
status: accepted
---

# ADR PAY-6 — Engine Keys on Enum, Not Master-Data Labels

> Up: [[DECISION_LOG]] · Related: [[Attendance Payroll Impact]]

**Context.** Attendance penalty/overtime *presentation* (labels, order, enable/disable) is customer-configurable, but the *business meaning* must be fixed and stable.

**Decision.** Engine logic keys on the **`AttendancePayrollKind` enum** (`Absence=1, Late=2, Shortage=3, Overtime=4`), mapping each to a configurable type **by code** (`ABSENCE`/`LATE`/`SHORTAGE`/`OVERTIME`) at sync time. Adding Overtime=4 kept values 1–3 stable → **no data migration**.

**Consequences.** Fixed meaning survives relabeling; the [[Master Data Engine|master-data]] catalog stays fully editable. Absence/Late/Shortage → Deduction; Overtime → Addition consumed by the existing `ADDITIONS` rule ([[Subproject 2E Attendance Daily Overtime Excuse]]).

## Related
[[Attendance Payroll Impact]] · [[Subproject 2D Attendance Deduction Records]] · [[ADR-No-Duplicate-Fields]] · [[DECISION_LOG]]
