---
title: Payroll Types Scope Cutoff
aliases: [Sub-project 1, Payroll Types, Selection Scope, Cutoff]
tags: [spec, payroll, finance]
---

# Sub-project 1 — Payroll Types + Selection Scope + Cutoff

> Source: `docs/superpowers/specs/2026-06-30-payroll-types-scope-cutoff-design.md` (+ plan + VERIFICATION). **Shipped & deployed.** The foundation the whole operational layer stands on.
> Up: [[Specs Index]] · Engine: [[Scope Engine]] · [[Snapshot and Versioning]]

## What it decided
Turn `PayrollDefinition`/`Version` into a configurable, customer-extensible **Payroll Type** with a pluggable [[Scope Engine]], calc settings, cutoff config, and config versioning (clone/publish/simulate). **No new "type" entity — the definition *is* the type.**

## Design decisions (embedded ADRs)
- **D1** — Payroll Type = enriched `PayrollDefinition` (+ version); no parallel entity ([[ADR-No-Duplicate-Fields]]).
- **D2** — New config on the **immutable `PayrollDefinitionVersion`**, not the mutable header.
- **D3** — Hot/queryable = typed columns; flexible/advanced = JSON.
- **D4** — `Category` + `ExportFormat` become **master-data catalogs** (`PayrollTypeCategory`, `PayrollExportFormat`), not enums. A genuinely new file *format* still needs a code handler ([[Master Data Engine]]).
- **D5** — Selection scope = **dimension registry + pluggable providers** ([[Scope Engine]], [[ADR-Provider-Registry-Pattern]]).
- **D6** — 8 dimensions backed, 7 registered-but-disabled.
- **D7** — Every run **snapshots its resolved population** ([[Snapshot and Versioning]]).

## Data model highlights
Typed columns on `PayrollDefinitionVersion`: `CutoffDay` (1–31), `DayBasis` (`CalendarMonth`/`Fixed30`/`WorkingDays`), `ClosingDate`, `PaymentDate`, `CarryToNextPeriod`, `DefaultExportFormatId`, `EffectiveFrom/To`, `IsSimulation`. JSON columns: `SelectionScopeJson`, `CalcSettingsJson` (include-toggles), `PaymentMethodScopeJson`. New table `engine_payroll_run_population`.

## Deferred enforcement
Cutoff + calc toggles are **persisted/UI-editable now but enforced later** (carry-over needs dated transactions, which arrive in [[Payroll Additions Deductions Overview|sub-project 2]]). **Only calc semantic wired: `DayBasis` proration.**

## Verification
151/151 tests green; clean build; 64/64 frontend pages. Migration application + DI/endpoint/immutability smoke deferred as manual (need live DB/browser). Re-seed master-data **before** `StandardPayrollSeeder`.

## Related
[[Scope Engine]] · [[Snapshot and Versioning]] · [[Payroll Engine]] · [[Financial Engine Redesign Master]]
