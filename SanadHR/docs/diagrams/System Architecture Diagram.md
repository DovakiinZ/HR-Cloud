---
title: System Architecture Diagram
aliases: [Architecture Diagram, Stack Diagram]
tags: [diagram, architecture]
---

# System Architecture Diagram

> Up: [[Diagrams Index]] · Explained in: [[Architecture Overview]] · [[Clean Architecture Layers]]

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

**Dependency rule:** dependencies point **inward only** — Domain depends on nothing; API is the edge. Detail: [[Clean Architecture Layers]].

## Related
[[Architecture Overview]] · [[Database Relationships]] · [[Deployment and Infrastructure]]
