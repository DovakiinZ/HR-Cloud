---
title: Configuration over Hardcoding
aliases: [Configurable over Hardcoded, Configuration First, No Hardcoded Logic]
tags: [principle, cross-cutting]
---

# Configuration over Hardcoding

> The defining product philosophy: if a client policy *could* differ, it is **data, not code**.
> Up: [[CLAUDE]] · Engine: [[Master Data Engine]] · Governance: [[ADR-No-Duplicate-Fields]]

SanadHR is an **HR Operating System**, not fixed HR software. Leave types, request types, allowance/deduction types, document types, workflows, dashboard/report templates, attendance policies, shifts, payroll definitions — all configurable, no hardcoded business logic.

Realised by: [[Master Data Engine]] (generic catalogs), [[Rule Engine]] (no-code policy), [[Workflow Engine]] (no-code approvals), [[Snapshot and Versioning]] (versioned policy). New catalog = new `MasterDataObjectType`, never a new table.

## Related
[[Master Data Engine]] · [[Cross-Cutting Rules]] · [[Reproducibility]] · [[CLAUDE]]
