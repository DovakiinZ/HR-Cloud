---
title: Scope Engine
aliases: [Selection Scope, Scope, Population Resolution]
tags: [architecture, engine, finance]
---

# Scope Engine

> Resolves **which employees** a payroll run includes — a dimension registry with pluggable per-module providers.
> Up: [[Financial Calculation Engine]] · Spec: [[Payroll Types Scope Cutoff]]

`IScopeEngine` (`ScopeEngine`) + `IScopeDimensionProvider`. Payroll calls `IScopeEngine` only and is **never coupled** to Employee columns — each owning module supplies its dimension's resolver, discovered via `AddScopeProvidersFromAssembly` ([[Cross-Module Integration|the provider/registry pattern]]).

## Backed dimensions (active)
Department, Branch, Job Title, Employment Type, Contract Type, Payment Method, Status, Nationality (+ an "all active" base population).
Registered but disabled-with-note: Tag, CostCenter, Grade, Shift, Project, BusinessUnit, Company.

## Resolution algebra
- Start = base population (mode `All`) or empty (`Criteria`).
- Includes: **OR within a dimension, AND across dimensions**; explicit include-ids unioned.
- Excludes subtracted — **exclude always wins**.
- A referenced dimension with no provider is **skipped with a warning**, never silently emptying the result.
- Malformed/missing `SelectionScopeJson` degrades to mode `All` (never throws) — a bad config can't silently empty a payroll.

Result frozen into the run population — see [[Snapshot and Versioning]].

## Related
[[Financial Calculation Engine]] · [[Payroll Types Scope Cutoff]] · [[Payroll Engine]] · [[ADR-Provider-Registry-Pattern]]
