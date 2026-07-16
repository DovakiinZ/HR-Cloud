---
title: Test Suite
aliases: [Tests, Testing, xUnit]
tags: [testing, quality]
---

# Test Suite

> ~161 backend xUnit tests across 3 projects (`backend/tests/`). Finance-heavy — the [[Financial Calculation Engine]] carries the deepest coverage.
> Up: [[Architecture Index]] · Status: [[IMPLEMENTATION_STATUS]] · Practice: [[TDD]]

| Project | ~Methods | Covers |
|---|---|---|
| `HR.Domain.Finance.Tests` | ~127 | expression/rule engine, dependency graph, Money, run + transaction [[Payroll Run State Machine|state machines]], period resolver, calc settings, validation, payslip↔ledger mapper, transaction service/consumer/merge/reversal/persistence, payroll types, day-basis proration, [[Scope Engine|scope]], permission merge, background execution context, attendance-payroll integration ([[Attendance Payroll Impact]]) |
| `HR.Modules.Employees.Tests` | ~15 | `EndOfServiceCalculator` (Saudi EOS math), leave accrual, employee scope providers |
| `HR.Modules.Workflows.Tests` | ~19 | workflow execution, graph validator, request conditions (+ shared `TestHarness`) |

Attendance-payroll integration suite (in Finance tests): `AttendanceDeductionRunTests`, `AttendanceDeductionSyncServiceTests`, `AttendanceExcuseExecutorTests`, `AttendanceOvertimeSyncTests`, `AttendanceReferenceEntityTests`, `AttendanceWageCalculatorTests`.

Payroll math must be **deterministic** (same inputs → same outputs) — see [[Reproducibility]]. TDD is required for [[Employees]], [[Workflows]], [[Payroll Engine|Finance/Payroll]].

## Related
[[TDD]] · [[Financial Calculation Engine]] · [[CLAUDE]]
