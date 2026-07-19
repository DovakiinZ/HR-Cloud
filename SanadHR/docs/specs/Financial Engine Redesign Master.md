---
title: Financial Engine Redesign Master
aliases: [Payroll Engine Redesign Master, Master Spec]
tags: [spec, payroll, finance]
---

# Financial Engine Redesign — Master Spec

> Source: `docs/superpowers/specs/2026-06-30-payroll-engine-redesign-master.md` (updated 2026-07-02). The living roadmap that decomposes the payroll build.
> Up: [[Specs Index]] · Engine: [[Financial Calculation Engine]]

## Goal
Build a complete **operational payroll layer** on top of the mature [[Financial Calculation Engine]] (Passes 1–4): configurable payroll types, rich employee selection, visible/traceable additions & deductions, attendance-deduction sync, run lifecycle with exclusions, payslip PDFs, and exports. **No mock data, no placeholders.** The engine is **"preserve, do not rewrite."**

## Core substrate (already built)
[[Immutable Ledger]] · [[Rule Engine]] (stored AST) · [[Dependency Graph Execution]] · [[Snapshot and Versioning]] (versioned definitions) · [[Payroll Run State Machine]] · `PayrollFactProvider` · supporting [[Documents|document]], [[Master Data Engine|master-data]], and [[Completion Effects Engine|completion-effects]] engines.

## Cross-cutting principles (every sub-project)
1. **Immutability & reproducibility** — config changes publish a new version; runs pin versions + snapshot population.
2. **No duplicate fields** — new catalogs are `MasterDataObjectType`s, not tables ([[ADR-No-Duplicate-Fields]]).
3. **Typed columns for hot fields; JSON for flexible settings.**
4. **Decoupling via providers/registries** ([[ADR-Provider-Registry-Pattern]]).
5. **Traceability — no hidden computed deductions.**
6. **Compatibility** with the run state machine / ledger / snapshot contracts.

## Decomposition (build order)
1. Payroll Types + Scope + Cutoff → [[Payroll Types Scope Cutoff]]
2. Additions & Deductions + Attendance sync → [[Payroll Additions Deductions Overview]] (2A/2C/2D/2E)
3. Run engine wiring + run details
4. Payslips · 5. Exports · 6. Run void/amend/reissue → [[Payroll Run Operations Roadmap]]

## Programme acceptance
Admin creates a Monthly payroll type, sets cutoff=27, creates a run for a department; the system includes matching employees and excludes invalid ones with reasons; attendance deductions sync as visible records; additions/deductions show separately; gross/net compute; payslip PDF prints; exports succeed; approved payroll stores payslips in employee documents.

## Related
[[Financial Calculation Engine]] · [[Payroll Engine]] · [[Specs Index]] · [[DECISION_LOG]]
