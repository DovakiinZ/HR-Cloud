---
title: Architecture Overview
aliases: [System Architecture, Overview]
tags: [architecture]
---

# Architecture Overview

> High-level architecture of SanadHR. See [[CLAUDE]] for rules, [[Database Design]] for schema, [[DOMAIN_MAP]] for business logic.
> Up: [[Architecture Index]]

SanadHR is a **Modular Monolith** built on **[[Clean Architecture Layers|Clean Architecture]]** and **Domain-Driven Design**, with **CQRS** and an **event-driven** internal backbone. One deployable; internally split into bounded-context [[MODULE_INDEX|modules]].

See the rendered diagram: [[System Architecture Diagram]].

```
┌──────────────────────────────────────────────┐
│  Frontend — Next.js 16 (Vercel)              │
│  App Router · shadcn/ui · React Flow · RHF+Zod │
└───────────────────────┬──────────────────────┘
                        │ REST (JWT)
┌───────────────────────▼──────────────────────┐
│  HR.Api  (ASP.NET Core Web API)              │
│  Controllers · Auth · Swagger · CORS         │
├──────────────────────────────────────────────┤
│  HR.Application (CQRS: Commands/Queries)      │
│  Handlers · Validation · Contracts · Events   │
├──────────────────────────────────────────────┤
│  HR.Modules  (17 bounded contexts)           │
│  Attendance · Payroll · Workflows · ...       │
├──────────────────────────────────────────────┤
│  HR.Domain (entities, engines, rules)        │
│  Formula · Rule · Ledger · Workflow engines   │
├──────────────────────────────────────────────┤
│  HR.Infrastructure                           │
│  EF Core · Dapper · S3/R2 · Redis · Hangfire  │
└───────────────────────┬──────────────────────┘
                        │
        ┌───────────────┼────────────────┐
        ▼               ▼                ▼
  PostgreSQL 16     AWS S3 / R2      Hangfire jobs
  (Azure Flexible)  (documents)      (background)
```

**Dependency rule:** Domain depends on nothing. Application depends on Domain. Infrastructure implements Application/Domain contracts. API depends on Application. Dependencies point **inward only**. Detail: [[Clean Architecture Layers]].

---

## The defining idea

**[[Payroll Engine|Payroll]] is one app on top of a general [[Financial Calculation Engine]].** Rather than special-cased salary math, money movement is modeled as an **[[Immutable Ledger|immutable ledger]]** driven by a **[[Rule Engine|stored-AST rule engine]]**, executed in **[[Dependency Graph Execution|dependency order]]** against **[[Snapshot and Versioning|versioned, snapshotted]]** definitions, through a **[[Payroll Run State Machine|run state machine]]**. This makes every payroll number reproducible and every correction an auditable reversal.

## Signature engines

- **[[Formula Engine]]** — evaluates dynamic salary/calculation expressions from configuration.
- **[[Rule Engine]]** — configurable business rules; no-code policy definitions (source + AST).
- **[[Immutable Ledger]]** — append-only entries; reversals instead of edits; full audit.
- **[[Workflow Engine]]** — state machines with a visual no-code builder (React Flow front-end).
- **[[Snapshot and Versioning]]** — captures payroll runs and policy versions for historical fidelity.
- **[[Dependency Graph Execution]]** — orders interdependent calculations correctly.
- **[[Scope Engine]]** — resolves which employees a run includes (pluggable dimension providers).
- **[[Completion Effects Engine]]** — plug-in side effects on request/workflow completion.
- **[[Master Data Engine]]** — one generic table powering every configurable catalog.

## Key design patterns

Clean Architecture · DDD bounded contexts · CQRS (MediatR) · Event-Driven ([[Cross-Module Integration|domain events]]) · Modular Monolith · [[Immutable Ledger|Immutable Ledger]] · [[Snapshot and Versioning|Snapshot/Versioning]] · State Machine · Repository + Unit of Work (EF Core) · Background Processing (Hangfire) · API-first (Swagger).

## Related
[[Tech Stack]] · [[Multi-Tenancy]] · [[Cross-Module Integration]] · [[DECISION_LOG]] · [[MODULE_INDEX]]
