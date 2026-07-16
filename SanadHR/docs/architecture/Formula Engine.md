---
title: Formula Engine
aliases: [Expression Engine, AST, Formula Evaluation]
tags: [architecture, engine, finance]
---

# Formula Engine

> Parses and evaluates dynamic calculation expressions (the AST behind every [[Rule Engine|rule]]).
> Up: [[Financial Calculation Engine]]

Location: `HR.Domain/Engines/Finance/Expressions/`.

- **AST** (`Ast.cs`) — `Expr` record hierarchy: `LiteralExpr`, `VariableExpr`, `UnaryExpr`, `BinaryExpr`, `FunctionCallExpr`.
- **Serialization** — `AstJson.cs` stores the compiled tree as JSON on each `Rule`.
- **Pipeline** — `ExpressionParser` → `AstJson` → `ExpressionEvaluator` against an `EvaluationContext`, with `FunctionRegistry` (built-in + tenant formula functions), `RuleValue`, and validation (`Validation.cs`).
- Deterministic and pure — same inputs yield same outputs ([[Reproducibility]]).

Tested heavily by `ExpressionEngineTests` / `RuleEngineCoreTests` — see [[Test Suite]].

## Related
[[Rule Engine]] · [[Dependency Graph Execution]] · [[Financial Calculation Engine]]
