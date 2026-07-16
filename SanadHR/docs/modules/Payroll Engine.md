---
title: Payroll Engine
aliases: [Payroll, Payroll Module, HR.Modules.Payroll]
tags: [module, finance]
---

# Payroll Engine

> The payroll **application** built on the [[Financial Calculation Engine]]. The module (`HR.Modules.Payroll`) is a thin HTTP surface; the heavy logic lives in `HR.Infrastructure/Engines/Finance`.
> Up: [[MODULE_INDEX]] · Lifecycle: [[Payroll Lifecycle]] · Specs: [[Specs Index]]

## Purpose
Run payroll for configurable **[[Payroll Types Scope Cutoff|payroll types]]** over selected employee populations, turning salary components + [[Payroll Additions and Deductions|additions/deductions]] + [[Attendance Payroll Impact|attendance impacts]] into reproducible, ledger-posted results — with an auditable [[Payroll Run State Machine|run lifecycle]].

## Architecture
`HR.Modules.Payroll` = `PayrollController` + DTOs (`PayrollDtos`, `PayrollTransactionDtos`, `PayrollTypeDtos`) + DI registering the Infrastructure engines. It stands on the [[Financial Calculation Engine]] ([[Immutable Ledger]] · [[Rule Engine]] · [[Dependency Graph Execution]] · [[Snapshot and Versioning]] · [[Scope Engine]]).

## Entities
`PayrollDefinition` (+ `PayrollDefinitionVersion`), `RuleSet` (+ `RuleSetVersion` + `Rule`), `PayrollRun` (+ `PayrollRunItem`, `PayrollRunPopulation`), `PayrollPayslip`, `PayrollTransaction` (+ `PayrollTransactionAttendanceReference`), `FinancialLedgerEntry`. Detail: [[Financial Calculation Engine]], [[Database Design]].

## Services
`IPayrollRunEngine`, `IPayrollPreviewEngine`, `IPayrollValidationEngine`, `IPayrollExecutionEngine`, `IPayrollFactProvider`, `IPayrollTransactionService` / `Consumer` / `ReversalService`, `IPayrollTypeService`, `IAttendancePayrollSyncService`, `IStandardPayrollSeeder`. → [[Financial Calculation Engine]]

## Events
`PayrollRunCreated/Calculated/Validated/Approved/ExecutionStarted/PayslipPosted/Completed/ExecutionFailed` via `IDomainEventPublisher`. Subscriber: `PayrollEventLogHandler`. → [[Cross-Module Integration]]

## Dependencies
[[Attendance]] (penalties/overtime → transactions), [[Loans]] + [[Expenses]] (feed deductions/ledger), [[Employees]] (population/facts via [[Scope Engine]]), [[Documents]] (payslips), [[Master Data Engine]] (types/categories/export formats). Never touches other modules' tables directly.

## API
`api/payroll` (runs, transactions, types, ledger); `api/payroll/attendance-deductions/sync` (2D sync-now). → [[API Endpoint Map]]. Frontend: `/payroll`, `/payroll/runs/[id]`, `/payroll/additions`, `/payroll/deductions`, `/settings/payroll/*`.

## Current Status
✅ Engine (P1–P4) + sub-projects 1, 2A, 2C, 2D shipped & deployed. 🔧 2E built (overtime/rates/excuse), not deployed. See [[IMPLEMENTATION_STATUS]].

## Future Work
Run details/quick-actions (3), payslips (4), exports (5), run void/amend/reissue (6), GOSI packs → [[Payroll Run Operations Roadmap]] · [[ROADMAP]].

## Related Notes
[[Financial Calculation Engine]] · [[Payroll Lifecycle]] · [[Payroll Run State Machine]] · [[Payroll Additions and Deductions]] · [[Attendance Payroll Impact]] · [[Payroll Types Scope Cutoff]] · [[Specs Index]]
