---
title: Cross-Module Integration
aliases: [Module Boundaries, Integration, Domain Events]
tags: [architecture]
---

# Cross-Module Integration

> How bounded contexts talk without reaching into each other's tables.
> Up: [[Architecture Index]] · Layers: [[Clean Architecture Layers]]

## Boundaries

- Each [[MODULE_INDEX|module]] is a **bounded context** (DDD): it owns its entities, rules, and persistence.
- Modules **do not** reach into each other's tables directly.
- Cross-module communication happens via **application-layer contracts** (interfaces) and **domain events**.
- Shared primitives live in [[Core]] / [[Platform]].

## Integration flows

| From → To | Interaction |
|---|---|
| [[Attendance]] → [[Payroll Engine]] | Attendance/overtime penalties become [[Payroll Additions and Deductions|PayrollTransaction records]] ([[Attendance Payroll Impact]]) |
| [[Loans]] → [[Payroll Engine]] | Loan deductions applied during payroll runs |
| [[Expenses]] → [[Payroll Engine]] | Reimbursements flow into payroll/ledger |
| [[Workflows]] → [[Tasks]] | Workflow states spawn approval tasks |
| [[Workflows]] → [[Notifications]] | State transitions trigger notifications |
| [[ESS]] → [[Workflows]] | Employee requests initiate workflow instances ([[Request Center]]) |
| Any → [[Documents]] | Certificates/payslips generated from templates |
| Any → [[Immutable Ledger]] | Financial events append to the ledger |

## The decoupling pattern

Payroll depends on **abstractions**, never on other modules' schemas. Three examples of the same pattern — a **registry populated by DI assembly scan**:

- [[Scope Engine]] — `IScopeEngine`; each module supplies its dimension resolver (`AddScopeProvidersFromAssembly`).
- [[Completion Effects Engine]] — `IEffectExecutor` discovered via `AddEffectExecutorsFromAssembly`.
- [[Workflow Engine]] — `IWorkflowStepHandler` per step type, resolved by DI collection.

New capability = new handler + one DI line; the consuming engine never changes (Open/Closed). This is [[ADR-Provider-Registry-Pattern|ADR PLAT-5]].

## Domain events

Finance publishes MediatR `INotification` records (`PayrollRunCreated`, `PayrollCalculated`, `PayslipPosted`, `PayrollCompleted`, …) via `IDomainEventPublisher`. Any module can subscribe with an `INotificationHandler` without the payroll engine knowing. Detail: [[Financial Calculation Engine]].

## Related
[[Architecture Overview]] · [[Cross-Cutting Rules]] · [[Financial Calculation Engine]]
