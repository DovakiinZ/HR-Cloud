---
title: ADR-Multi-Tenancy-Strategy
aliases: [ADR PLAT-3, Multi-Tenancy Decision]
tags: [adr, architecture]
status: accepted
---

# ADR PLAT-3 — App-Layer Multi-Tenancy

> Up: [[DECISION_LOG]] · Related: [[Multi-Tenancy]]

**Context.** A SaaS platform must isolate tenant data absolutely, while supporting multiple companies within one tenant.

**Decision.** **Application-layer isolation** — a single shared `ApplicationDbContext` with **EF Core global query filters** on a `TenantId` carried by every business entity (`TenantEntity`). Not database-per-tenant or schema-per-tenant.

**Consequences.** One schema, one migration path, simple ops. Isolation depends on the filter being applied — so a tenant-isolation test is part of the [[CLAUDE|Definition of Done]], and background jobs must establish an ambient tenant scope (`IBackgroundExecutionContext`).

## Related
[[Multi-Tenancy]] · [[Database Design]] · [[DECISION_LOG]]
