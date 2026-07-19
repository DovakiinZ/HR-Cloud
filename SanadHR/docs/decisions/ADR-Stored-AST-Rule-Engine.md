---
title: ADR-Stored-AST-Rule-Engine
aliases: [ADR FIN-2, Stored AST, Rule Engine Decision]
tags: [adr, finance]
status: accepted
---

# ADR FIN-2 — Stored-AST Rule Engine

> Up: [[DECISION_LOG]] · Related: [[Rule Engine]] · [[Formula Engine]]

**Context.** Payroll policy must be no-code, versioned, and reproducible; re-parsing source at run time risks drift and is slow.

**Decision.** Rules store **both authored source and a compiled AST JSON**. Evaluation runs the AST (via the [[Formula Engine]]) in [[Dependency Graph Execution|dependency order]]; money rounds `AwayFromZero`, 2 dp. Rule versions are immutable and pinned by a run.

**Consequences.** A historical run recomputes identically forever ([[Reproducibility]]). Authors write rules in any order; the engine orders execution. New functions register in `FunctionRegistry`.

## Related
[[Rule Engine]] · [[Formula Engine]] · [[Dependency Graph Execution]] · [[ADR-Versioned-Definitions]] · [[DECISION_LOG]]
