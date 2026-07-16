---
title: ADR-No-Duplicate-Fields
aliases: [ADR PLAT-4, Schema Governance, No Duplicate Fields]
tags: [adr, architecture, governance]
status: accepted
---

# ADR PLAT-4 — No Duplicate Fields / Configurable Catalogs

> Up: [[DECISION_LOG]] · Related: [[Master Data Engine]]

**Context.** Customers rename, add, reorder, and disable lookup values (categories, types, formats). Modeling each as a C# enum or a bespoke table causes duplication and churn.

**Decision.** New configurable catalogs are **new `MasterDataObjectType`s in the generic [[Master Data Engine|master-data]] table, never new tables or enums**. Reuse canonical fields via the Object Registry / Metadata / Master Data engines. Hot/queryable fields = typed columns; flexible settings = JSON.

**Consequences.** Customer-configurable without migrations. Engine logic keys on **stable codes**, not labels ([[ADR-Attendance-Penalty-Kind]]). Boundary: a genuinely new file *format* still needs a code handler even though the catalog entry is master data.

## Related
[[Master Data Engine]] · [[Configuration over Hardcoding]] · [[Payroll Types Scope Cutoff]] · [[DECISION_LOG]]
