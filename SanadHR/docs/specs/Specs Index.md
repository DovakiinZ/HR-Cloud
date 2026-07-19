---
title: Specs Index
aliases: [Specs, Specifications, Design Specs]
tags: [index, specs, payroll]
---

# 📐 Specs Index — Financial Engine Redesign

> The design specs & plans for the payroll/finance redesign. Source of truth on disk: `docs/superpowers/specs/*` and `docs/superpowers/plans/*` (14 files). Each spec ran a full **brainstorm → spec → plan → build → verify → ship** cycle.
> Up: [[Home]] · Engine: [[Financial Calculation Engine]] · Decisions: [[DECISION_LOG]]

## Vision & foundation
- [[Financial Engine Redesign Master]] — the master vision & decomposition (2026-06-30)
- [[Payroll Types Scope Cutoff]] — Sub-project 1: payroll types + scope + cutoff (shipped)

## Additions / Deductions series (Sub-project 2)
- [[Payroll Additions Deductions Overview]] — the philosophy change ("no hidden deductions")
- [[Subproject 2A Transaction Records]] — records + lifecycle + pages (shipped)
- [[Subproject 2C Consumption Posting Reversal]] — consume + post + reverse (shipped, PR #11)
- [[Subproject 2D Attendance Deduction Records]] — attendance → deduction records (shipped)
- [[Subproject 2E Attendance Daily Overtime Excuse]] — daily actions + overtime→addition + rates + excuse (built)

## Roadmap
- [[Payroll Run Operations Roadmap]] — the 8 enhancement areas → sub-projects 2E/3/4/5/6

## Status snapshot
| Sub-project | Status |
|---|---|
| Engine P1–P4 | ✅ shipped & deployed |
| 1, 2A, 2C, 2D | ✅ shipped & deployed |
| 2E | 🔧 built, not deployed |
| 3, 4, 5, 6 | 🗓️ planned |

Full matrix: [[IMPLEMENTATION_STATUS]].

## Related
[[Financial Calculation Engine]] · [[Payroll Engine]] · [[ROADMAP]] · [[DECISION_LOG]]
