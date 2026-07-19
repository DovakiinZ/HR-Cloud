---
title: Settlement Engine
aliases: [End of Service Engine, EOS Engine, Settlement Calculator]
tags: [architecture, engine, finance, saudi]
---

# Settlement Engine

> Computes Saudi **end-of-service** (نهاية الخدمة) settlements. Pure domain math + an infrastructure loader.
> Up: [[Architecture Index]] · Domain: [[End of Service]] · Module: [[Employees]]

- **Pure calculator:** `EndOfServiceCalculator` (`HR.Domain/Engines/Settlement/`) — `SettlementInput` → `SettlementResult`, encoding Saudi Labor Law (Articles 84 & 85 gratuity, scenario awards).
- **Loader:** `IEndOfServiceEngine` → `EndOfServiceEngine` (`HR.Infrastructure`) — loads the employee, resolves monthly wage + unpaid-leave days, delegates to the pure calculator.
- **Workflows:** `ITerminationWorkflow`, `IRestoreWorkflow` — see [[Termination and Restore]].

Known fix: EOS settlement 500 caused by non-UTC date normalization (commit a8f5e85). Tested by `EndOfServiceCalculatorTests` ([[Test Suite]]).

## Related
[[End of Service]] · [[Employees]] · [[Termination and Restore]] · [[Financial Calculation Engine]]
