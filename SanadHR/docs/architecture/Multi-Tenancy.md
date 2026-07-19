---
title: Multi-Tenancy
aliases: [Multi-Tenant, Tenant Isolation, Tenancy Model]
tags: [architecture, cross-cutting]
---

# Multi-Tenancy

> Every record is tenant-scoped; no cross-tenant leakage — ever.
> Up: [[Architecture Index]] · Module: [[Tenancy]] · DB: [[Database Design]]

SanadHR is **SaaS multi-tenant** with **application-layer isolation**:

- Every business entity carries a **tenant key** (`TenantId`); the base type is `TenantEntity`.
- All queries are **filtered by tenant** via EF Core **global query filters** on a single shared `ApplicationDbContext`.
- **Multi-company** is supported *within* a tenant (a tenant may own several companies).
- No cross-tenant data access is permitted — verified per endpoint (a tenant-isolation test is part of the [[CLAUDE|Definition of Done]]).

## Background jobs

Hangfire jobs (e.g. [[Payroll Engine|payroll]] batch execution) run outside a request, so there's no ambient user/tenant. `BackgroundExecutionContext` (`IBackgroundExecutionContext`) establishes an **ambient tenant scope** so each worker's `DbContext` still filters correctly. See [[Financial Calculation Engine]].

## Rule

> When adding an entity: include the tenant key and ensure query filters apply. When adding a query: it must be tenant-scoped. This is [[Cross-Cutting Rules|non-negotiable]].

## Related
[[Tenancy]] · [[Access Management]] · [[Database Design]] · [[Cross-Cutting Rules]] · [[ADR-Multi-Tenancy-Strategy]]
