---
title: ADR-Versioned-Definitions
aliases: [ADR FIN-4, Versioned Definitions, Frozen Population]
tags: [adr, finance]
status: accepted
---

# ADR FIN-4 — Versioned Definitions + Frozen Run Population

> Up: [[DECISION_LOG]] · Related: [[Snapshot and Versioning]]

**Context.** Policy and org data change over time; a past payroll must not change when they do.

**Decision.** `PayrollDefinition` owns **immutable `PayrollDefinitionVersion`s**; config changes publish a new version. A run **pins** its definition version + `RuleSetVersion` and **snapshots its resolved employee population** into `engine_payroll_run_population` at creation.

**Consequences.** Reproducibility and historical fidelity — future org/policy changes never rewrite history. Editing config = clone → edit draft → publish, never mutate in place.

## Related
[[Snapshot and Versioning]] · [[Scope Engine]] · [[Payroll Types Scope Cutoff]] · [[DECISION_LOG]]
