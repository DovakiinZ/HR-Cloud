---
title: ADR-Modular-Monolith
aliases: [ADR PLAT-1, Modular Monolith Decision]
tags: [adr, architecture]
status: accepted
---

# ADR PLAT-1 — Modular Monolith

> Up: [[DECISION_LOG]] · Related: [[Architecture Overview]]

**Context.** SanadHR spans many bounded contexts (17 modules) but is built by a small team for the Saudi SME→enterprise market.

**Decision.** Build a **modular monolith** — one deployable, internally split into bounded-context modules with clean boundaries and [[Cross-Module Integration|contract/event-based]] communication — rather than microservices.

**Consequences.** Module isolation and DDD discipline without distributed-systems overhead (no network hops, one DB, one deploy). Cross-module calls go through [[Clean Architecture Layers|application contracts]] and domain events, so a future extraction to services stays possible.

## Related
[[Clean Architecture Layers]] · [[Cross-Module Integration]] · [[DECISION_LOG]]
