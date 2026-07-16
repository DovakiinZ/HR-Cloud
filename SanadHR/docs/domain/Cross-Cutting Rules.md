---
title: Cross-Cutting Rules
aliases: [Business Rules, Cross-Cutting Business Rules]
tags: [domain, principle]
---

# Cross-Cutting Business Rules

> The rules that hold across every module. Enforced technically in the [[CLAUDE|operating manual]] and [[Development Standards]].
> Up: [[DOMAIN_MAP]]

- **Multi-tenancy** — every record tenant-scoped; no cross-tenant leakage. → [[Multi-Tenancy]]
- **Configurable over hardcoded** — policies (leave, overtime, payroll, requests) are data, not code. → [[Configuration over Hardcoding]] / [[Master Data Engine]]
- **Auditability** — every action carries who/when/source; financial data is immutable. → [[Immutable Ledger]] / [[Reproducibility]]
- **Versioning** — policies & salary structures keep full history; runs pin versions. → [[Snapshot and Versioning]]
- **Saudi-first** — Arabic RTL documents; local statutory rules ([[End of Service|EOS]], GOSI). → [[Arabic RTL]]
- **Approval-driven** — payroll, leave, terminations pass through workflows. → [[Workflow Engine]]

## Related
[[DOMAIN_MAP]] · [[CLAUDE]] · [[Development Standards]] · [[DECISION_LOG]]
