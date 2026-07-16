---
title: Rule Engine
aliases: [Rules, RuleSet, Calculation Rules]
tags: [architecture, engine, finance]
---

# Rule Engine

> Configurable, versioned calculation-rule library. No-code policy definitions stored as source + compiled AST.
> Up: [[Financial Calculation Engine]] · Decision: [[ADR-Stored-AST-Rule-Engine]]

`RuleSet` + `RuleSetVersion` + `Rule` (`HR.Domain/Engines/Finance/Entities/RuleSet.cs`).

- Each `Rule` stores both the **authored source** and the compiled **AST JSON** (evaluated by the [[Formula Engine]]).
- Execution order is derived from an inter-rule **[[Dependency Graph Execution|dependency graph]]**, not authoring sequence.
- `RuleSetVersion` makes rules **immutable and reproducible** — a [[Payroll Run State Machine|run]] pins a specific version forever ([[Snapshot and Versioning]]).
- Money rounding: `MidpointRounding.AwayFromZero`, 2 dp (`RuleEngineCore`).
- Impl: `IRuleEngine` → `RuleEngine` (DB-backed bridge to pure `RuleEngineCore`); `RuleSetEvaluator` evaluates a compiled set against a fact bag.

Seeded rules include `ADDITIONS` / `DEDUCTIONS` (sum recurring components) and the retired `ATTENDANCE_DED` (replaced by records — see [[Subproject 2D Attendance Deduction Records]]).

## Related
[[Formula Engine]] · [[Dependency Graph Execution]] · [[Financial Calculation Engine]] · [[Master Data Engine]]
