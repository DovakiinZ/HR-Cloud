---
title: IMPLEMENTATION_STATUS
aliases: [Implementation Status, Build Matrix, Module Status Matrix]
tags: [control, status, matrix]
updated: 2026-07-03
---

# 🧱 IMPLEMENTATION_STATUS — Build Matrix

> Per-module implementation reality: backend, frontend, tests, live-API wiring. Complements the narrative [[PROJECT_STATUS]] with a hard matrix.
> Up: [[Home]] · Modules: [[MODULE_INDEX]] · Roadmap: [[ROADMAP]]

Legend: ✅ done · 🟡 partial · 🔧 active · 🗓️ planned · 🔴 skeleton/mock

---

## Backend modules

| Module | Backend | Live API wired to FE | Tests | Notes |
|---|---|---|---|---|
| [[Identity]] | ✅ | ✅ | ✅ | JWT + refresh, deny-wins [[Access Management]] |
| [[Core]] | ✅ | ✅ | — | branches/departments/files |
| [[Employees]] | ✅ | ✅ | ✅ (EOS, scope) | reference module, CQRS |
| [[Attendance]] | ✅ | ✅ | ✅ | punches→records, [[Attendance Payroll Impact|payroll sync]] |
| [[Payroll Engine]] | ✅ | ✅ | ✅ (~127 finance) | app on [[Financial Calculation Engine]] |
| [[Workflows]] | ✅ | ✅ | ✅ (19) | FlowBuilder engine |
| [[Platform]] | ✅ | 🟡 | 🟡 | 30 controllers; broad surface |
| [[Tasks]] | ✅ | 🔴 mock | ✅ | **FE on `tasks-mock-data.ts`** |
| [[Settings]] | ✅ | ✅ | — | company settings |
| [[Tenancy]] | ✅ | n/a | — | isolation wiring |
| [[Loans]] | 🟡 | ✅ | — | payroll integration in progress |
| [[Expenses]] | 🟡 | ✅ | — | payroll/ledger flow in progress |
| [[Documents]] | ✅ | ✅ | — | QuestPDF, [[Document Platform]] |
| [[Reports]] | ✅ | ✅ | — | report builder + export |
| [[Dashboards]] | ✅ | ✅ | — | object-driven builder |
| [[Notifications]] | ✅ | ✅ | — | in-app + email |
| [[ESS]] | ✅ | ✅ | — | [[Request Center]] surface |

> Historical note: `backend/BACKEND.md` once listed 10 modules returning HTTP 501; most have since been implemented. [[Tasks]] is the main remaining mock-only frontend area.

---

## Financial engine / payroll (sub-project status)

| Sub-project | Scope | Status | Spec |
|---|---|---|---|
| Financial Engine P1–P4 | ledger, rule engine, run state machine, batch execution | ✅ shipped | [[Financial Engine Redesign Master]] |
| Sub-project 1 | payroll types + scope + cutoff | ✅ shipped & deployed | [[Payroll Types Scope Cutoff]] |
| 2A | transaction records + lifecycle + pages | ✅ shipped & deployed | [[Subproject 2A Transaction Records]] |
| 2C | consumption + posting + reversal | ✅ shipped (PR #11) | [[Subproject 2C Consumption Posting Reversal]] |
| 2D | attendance → deduction records | ✅ shipped & deployed | [[Subproject 2D Attendance Deduction Records]] |
| 2E | daily actions + overtime→addition + rates + excuse | 🔧 built, not deployed | [[Subproject 2E Attendance Daily Overtime Excuse]] |
| 3 | run details / quick actions | 🟡 partial | [[Payroll Run Operations Roadmap]] |
| 4 | payslips (PDF/print/store) | 🗓️ planned | [[Payroll Run Operations Roadmap]] |
| 5 | exports (Excel/PDF/CSV/TXT/bank) | 🗓️ planned | [[Payroll Run Operations Roadmap]] |
| 6 | run void / amend / reissue | 🗓️ planned | [[Payroll Run Operations Roadmap]] |

---

## Tests

~161 backend xUnit tests across 3 projects. Detail: [[Test Suite]].

| Project | ~Count | Focus |
|---|---|---|
| `HR.Domain.Finance.Tests` | ~127 | expression/rule engine, state machines, payroll transactions, attendance sync |
| `HR.Modules.Employees.Tests` | ~15 | EOS calculator, leave accrual, employee scope |
| `HR.Modules.Workflows.Tests` | ~19 | workflow execution, graph validator, request conditions |

---

## Schema

30 EF Core migrations (`InitialCreate` … `AttendancePayrollImpactPermission`). Full chronology: [[Migration History]].

Related: [[PROJECT_STATUS]] · [[ROADMAP]] · [[Changelog Index]]
