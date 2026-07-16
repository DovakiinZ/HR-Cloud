---
title: Employees
aliases: [Employee Module, HR.Modules.Employees, Core HR]
tags: [module]
---

# Employees

> The reference module — full CQRS, TDD, and the source of truth for people data. Employee lifecycle from onboarding to [[End of Service|settlement]].
> Up: [[MODULE_INDEX]] · Lifecycle: [[Employee Lifecycle]]

## Purpose
Manage the employee record and its lifecycle: onboarding, updates, termination trigger, EOS settlement preview, and employee scoping for other modules.

## Architecture
`HR.Modules.Employees` — CQRS (Create/Update/Delete/Terminate commands + validators, Get/Export queries), `EmployeeProjection`, `EmployeeScopeProviders` (feeds the [[Scope Engine]]). The [[Settlement Engine]] does EOS math.

## Entities
`Employee` (file lives in `HR.Domain/Entities/Employees/Employee.cs`, namespace `HR.Modules.Employees.Entities`), `EmployeeAllowance`, `EmployeeAddition`, `EmployeeDeduction`, `EmployeeRestoreRequest`, `TerminationSettlement` (+ items). Org: `Branch`, `Department`, `Position`, `Grade`, `CostCenter` ([[Org Structure]]).

## Services
CQRS handlers; `EmployeeScopeProviders` (scope dimensions); `IEndOfServiceEngine` / `EndOfServiceCalculator` ([[Settlement Engine]]); `ITerminationWorkflow` / `IRestoreWorkflow` ([[Termination and Restore]]).

## Events
Termination/restore flow through the [[Workflow Engine]] approval chain rather than emitting payroll-style domain events.

## Dependencies
[[Workflows]] (termination/restore approval), [[Documents]] (settlement PDF), [[Payroll Engine]] (settlement expense, population/facts), [[Master Data Engine]] (job titles, nationalities, contract/payment types), [[Access Management]] (scoping).

## API
`api/employees` (CRUD/list/export/terminate), `api/employees/{id}/documents`, `api/terminations`, `api/restores`. → [[API Endpoint Map]]. Frontend: `/employees`, `/employees/new`, `/employees/[id]` (+ `/edit`, `/settlement`), `/employees/terminations`. Excel export via ClosedXML (permission-gated).

## Current Status
✅ Built, deployed, TDD (`EndOfServiceCalculatorTests`, `EmployeeScopeProvidersTests`, leave accrual). Live API wired.

## Future Work
Bulk import / data-migration tooling → [[ROADMAP]].

## Related Notes
[[Employee Lifecycle]] · [[End of Service]] · [[Termination and Restore]] · [[Org Structure]] · [[Settlement Engine]] · [[Scope Engine]]
