---
title: ADR-Provider-Registry-Pattern
aliases: [ADR PLAT-5, Provider Registry, DI Assembly Scan]
tags: [adr, architecture]
status: accepted
---

# ADR PLAT-5 — Decoupling via Provider/Registry + DI Assembly Scan

> Up: [[DECISION_LOG]] · Related: [[Cross-Module Integration]]

**Context.** Payroll needs data from other modules (which employees, what impacts, what side effects) but must not depend on their schemas.

**Decision.** Payroll depends on **abstractions**; each owning module supplies its implementation, discovered via a **DI assembly scan** into a registry. Three instances of the same pattern: [[Scope Engine]] (`AddScopeProvidersFromAssembly`), [[Completion Effects Engine]] (`AddEffectExecutorsFromAssembly`), [[Workflow Engine]] step handlers.

**Consequences.** New capability = new handler/provider + one DI line; the consuming engine never changes (Open/Closed). No module reaches into another's tables.

## Related
[[Cross-Module Integration]] · [[Scope Engine]] · [[Completion Effects Engine]] · [[Workflow Engine]] · [[DECISION_LOG]]
