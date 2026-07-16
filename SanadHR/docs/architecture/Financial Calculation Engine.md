---
title: Financial Calculation Engine
aliases: [Financial Engine, Finance Engine, Calculation Engine]
tags: [architecture, engine, finance]
---

# Financial Calculation Engine

> The platform SanadHR's money runs on. **[[Payroll Engine|Payroll]] is one application on top of it.** Enterprise design: enrich, do not rewrite — the engine is mature.
> Up: [[Architecture Index]] · App: [[Payroll Engine]] · Specs: [[Financial Engine Redesign Master]]

Location: `HR.Domain/Engines/Finance/` (pure logic) + `HR.Infrastructure/Engines/Finance/` (DB-backed implementations). Interfaces in `HR.Application/Engines/Finance/`.

## Purpose

Model **all money movement** as an auditable, reproducible, versioned pipeline — not special-cased salary math. Any process (payroll, settlement, bonuses, off-cycle) is expressed as rules evaluated against facts, posted to an immutable ledger.

## Core concepts (the substrate)

- **[[Immutable Ledger]]** — append-only `FinancialLedgerEntry`; corrections are reversing entries, never edits.
- **[[Rule Engine]]** — `RuleSet` / `RuleSetVersion` / `Rule` storing source + compiled **AST JSON**; money rounding `MidpointRounding.AwayFromZero`, 2 dp.
- **[[Formula Engine]]** — the expression parser/evaluator (`Ast`, `ExpressionParser`, `ExpressionEvaluator`, `FunctionRegistry`).
- **[[Dependency Graph Execution]]** — rules evaluated in topological order, not authoring sequence.
- **[[Snapshot and Versioning]]** — `PayrollDefinition` + `PayrollDefinitionVersion` (the versioned policy a run pins); runs freeze their population.
- **[[Payroll Run State Machine]]** — `PayrollRun`: Draft → Preview → Validated → PendingApproval → Approved → Executing → Completed (+ Failed/Cancelled/Locked/Archived).
- **[[Scope Engine]]** — resolves which employees a run includes.

## The four passes (P1–P4)

The mature pipeline, referenced by the [[Financial Engine Redesign Master|master spec]] as the already-built substrate:

1. **Versioned definitions** — a run pins an immutable `PayrollDefinitionVersion` + `RuleSetVersion`.
2. **Scope + facts** — `PayrollFactProvider` resolves the population and builds each employee's **fact bag** (basic, capped allowances, additions − deductions − GOSI, attendance aggregates).
3. **Rule evaluation** — dependency-ordered AST evaluation produces frozen payslip components at **Calculate/Preview**.
4. **Posting** — batch, resumable, idempotent posting of components to the [[Immutable Ledger]] at **Execute**, with reversals.

The **operational payroll layer** (sub-projects 1–6) is what's *added* on top — see [[Payroll Engine]] and [[Specs Index]].

## Services

| Service | Role |
|---|---|
| `IRuleEngine` → `RuleEngine` | loads rule-set version, compiles expressions, evaluates in dependency order |
| `IFinancialLedger` → `FinancialLedger` | append-only writer; `ReverseAsync` writes a counter-entry |
| `IPayrollExecutionEngine` → `PayrollExecutionEngine` | **batch orchestrator** — one `PayrollRunItem` per payslip, bounded concurrency, resumable/idempotent |
| `IPayrollFactProvider` → `PayrollFactProvider` | builds per-employee fact bag |
| `IPayrollRunEngine`, `IPayrollPreviewEngine`, `IPayrollValidationEngine` | run lifecycle, dry-run preview, pre-execution validation |
| `IStandardPayrollSeeder` | seeds default `STD_MONTHLY` rule set + definition so payroll runs out of the box |
| `IAttendancePayrollSyncService` (+ `AttendanceWageCalculator`) | attendance penalties/overtime → [[Payroll Additions and Deductions|PayrollTransaction]] records ([[Attendance Payroll Impact]]) |

Scheduling: `HangfirePayrollExecutionScheduler` / `InProcessPayrollExecutionScheduler` (Hangfire-ready, currently in-process). Tenant scope for jobs via `IBackgroundExecutionContext` — see [[Multi-Tenancy]].

## Events

MediatR `INotification` records in `PayrollEvents.cs`: `PayrollRunCreated`, `PayrollCalculated`, `PayrollValidated`, `PayrollApproved`, `PayrollExecutionStarted`, `PayslipPosted`, `PayrollCompleted`, `PayrollExecutionFailed`. Published via `IDomainEventPublisher` (MediatR-backed, outbox-swappable). Only current subscriber: `PayrollEventLogHandler`. Design is decoupled — any module can subscribe ([[Cross-Module Integration]]).

## Status
✅ Passes 1–4 shipped and deployed (~127 finance tests — [[Test Suite]]). Operational layer: see [[IMPLEMENTATION_STATUS]].

## Related
[[Payroll Engine]] · [[Immutable Ledger]] · [[Rule Engine]] · [[Dependency Graph Execution]] · [[Snapshot and Versioning]] · [[Payroll Run State Machine]] · [[DECISION_LOG]] · [[Financial Engine Redesign Master]]
