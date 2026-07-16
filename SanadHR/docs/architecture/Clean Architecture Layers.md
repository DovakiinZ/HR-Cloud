---
title: Clean Architecture Layers
aliases: [Clean Architecture, Layers, Solution Layout]
tags: [architecture]
---

# Clean Architecture Layers

> The 5 core .NET projects and the inward-only dependency rule.
> Up: [[Architecture Index]] · Overview: [[Architecture Overview]]

Root: `backend/src`. Target: **.NET 8**. `HR.Modules` is a *folder* of 17 module projects; the four core library projects are `HR.Api`, `HR.Application`, `HR.Domain`, `HR.Infrastructure`.

| Layer | Project | Responsibility |
|---|---|---|
| Presentation | `HR.Api` | Composition root / host. `Program.cs`, middleware (`ExceptionHandlingMiddleware`), `CurrentUserService`, Swagger/JWT/Hangfire wiring. References Infrastructure + Application + **all 17 modules**. |
| Application | `HR.Application` | Contracts & orchestration. Interfaces (`IApplicationDbContext`, engine interfaces like `IRuleEngine`, `IFinancialLedger`), MediatR behaviors, **domain-event records**, DTOs, `BaseApiController`, `RequirePermissionAttribute`. References Domain. |
| Domain | `HR.Domain` | Pure domain. Entities, value objects, enums, and self-contained engine logic (Finance rule AST, state machines, EOS calculator). **Zero project references.** |
| Infrastructure | `HR.Infrastructure` | Implementations. EF Core `ApplicationDbContext` (~153 DbSets), configurations, migrations, engine impls (`RuleEngine`, `FinancialLedger`, `PayrollExecutionEngine`, seeders, sync services), Redis cache, R2 storage, domain-event publisher, audit. References Application. |
| Modules | `HR.Modules/*` | 17 vertical feature slices (controllers, CQRS handlers, DTOs, per-module DI). Reference Application (+ Infrastructure, except app-only modules). |

**Dependency direction:** `HR.Api → (all Modules) → HR.Infrastructure → HR.Application → HR.Domain`. Dependencies point **inward only** — Domain is the center, Api is the edge. This is the discipline the [[CLAUDE|operating manual]] enforces.

> App-only modules (reference Application but **not** Infrastructure): [[Dashboards]], [[Documents]], [[ESS]], [[Notifications]], [[Reports]].

## Where the work actually lives

Engine **interfaces** live in `HR.Application/Engines/*`; **implementations** in `HR.Infrastructure/Engines/*`. Most module projects are thin (controllers + DTOs + DI) because heavy logic is in the engines. This is why a "module" note and an "engine" note are different things — see [[Financial Calculation Engine]] vs [[Payroll Engine]].

## Related
[[Architecture Overview]] · [[Cross-Module Integration]] · [[Multi-Tenancy]] · [[Database Design]] · [[MODULE_INDEX]]
