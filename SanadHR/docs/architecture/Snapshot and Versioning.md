---
title: Snapshot and Versioning
aliases: [Versioning, Snapshots, Versioned Definitions, PayrollDefinition]
tags: [architecture, engine, finance]
---

# Snapshot & Versioning

> Historical reproducibility: config changes publish a **new version**; runs **pin** versions and **snapshot** their population, so future org/policy changes never rewrite history.
> Up: [[Financial Calculation Engine]] · Decision: [[ADR-Versioned-Definitions]]

## Versioned definitions

`PayrollDefinition` + `PayrollDefinitionVersion` — the logical definition owns immutable versions; `CurrentVersionId` = the published one. This *is* the "Payroll Type" ([[Payroll Types Scope Cutoff]]). A [[Payroll Run State Machine|run]] pins **both** the `PayrollDefinitionVersion` **and** the `RuleSetVersion` — reproducible forever.

**Config versioning operations:**
- **Clone** — published → new Draft.
- **Publish** — Draft → Published; supersedes prior, sets `PublishedAt`, closes prior `EffectiveTo`; immutable thereafter.
- **Simulate** — dry-run a Draft (`IsSimulation`) through `PayrollPreviewEngine`, no DB/ledger writes.

## Frozen run population

Every run snapshots its resolved employees into `engine_payroll_run_population` (one row per employee, with `IsIncluded` + `ExclusionReasonCode`) at creation. Org changes afterwards never alter a historical run. Resolution done by the [[Scope Engine]].

## Payslip snapshot

`PayrollPayslip` is an immutable per-employee result with the full component breakdown (`ComponentsJson`).

## Related
[[Financial Calculation Engine]] · [[Payroll Run State Machine]] · [[Scope Engine]] · [[Reproducibility]] · [[Payroll Types Scope Cutoff]]
