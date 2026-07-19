---
title: TDD
aliases: [Test-Driven Development, Development Process]
tags: [testing, process]
---

# TDD — Test-Driven Development

> Required for critical modules: [[Employees]], [[Workflows]], [[Payroll Engine|Finance/Payroll]]. Failing test first, then code.
> Up: [[Architecture Index]] · Suite: [[Test Suite]]

Each payroll sub-project was built with a full **brainstorm → spec → plan → subagent build → verify → ship** cycle, TDD throughout (see [[Specs Index]] plan files). Expectations:

- Unit tests cover domain logic, formula/rule evaluation, and payroll math.
- Payroll requires **deterministic, reproducible** cases ([[Reproducibility]]).
- New endpoints: at least one happy-path + one tenant-isolation test ([[Multi-Tenancy]]).
- Frontend: validate forms with Zod; test critical builder flows (workflow, document, dashboard).

## Related
[[Test Suite]] · [[CLAUDE]] · [[Development Standards]]
