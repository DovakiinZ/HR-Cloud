---
title: Payroll Lifecycle
aliases: [Payroll Flow, Payroll Process]
tags: [domain, lifecycle, finance]
---

# Payroll Lifecycle

> The business view of a payroll run. Engineering view: [[Payroll Run State Machine]] · [[Financial Calculation Engine]].
> Up: [[DOMAIN_MAP]] · Module: [[Payroll Engine]]

```
Inputs → Rule Evaluation → Preview/Snapshot → Approval → Ledger Post → Payslip
```

| Step | Detail |
|---|---|
| **Inputs** | Base salary, [[Payroll Additions and Deductions|additions/deductions]], attendance/overtime ([[Attendance Payroll Impact]]), loans, expenses, GOSI. |
| **Rule Evaluation** | The [[Rule Engine]]/[[Formula Engine]] compute each component in [[Dependency Graph Execution|dependency order]]. |
| **Preview & Snapshot** | A [[Snapshot and Versioning|snapshot]] captures all inputs/outputs for reproducibility. |
| **Approval** | Multi-step approval validates the run ([[Payroll Run State Machine]]). |
| **Ledger Post** | [[Immutable Ledger|Immutable, append-only]] entries written; corrections = reversing entries. |
| **Payslip** | The [[Documents|document engine]] generates an RTL payslip (planned — [[Payroll Run Operations Roadmap]]). |

**Key rules**
- Payroll is **reproducible** — same inputs → same outputs ([[Reproducibility]]).
- Ledger entries are **never edited or deleted**.
- Policies are **versioned**; a run uses the policy version active for its period.
- Attendance/overtime, loans, and expenses **feed** payroll ([[Cross-Module Integration]]).

## Related
[[Payroll Engine]] · [[Financial Calculation Engine]] · [[Payroll Run State Machine]] · [[Payroll Additions and Deductions]] · [[Attendance Payroll Impact]]
