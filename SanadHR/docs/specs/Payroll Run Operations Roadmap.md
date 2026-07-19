---
title: Payroll Run Operations Roadmap
aliases: [Run Operations Roadmap, Payroll Enhancement Roadmap]
tags: [spec, payroll, roadmap]
---

# Payroll Run Operations — Enhancement Roadmap

> Source: `docs/superpowers/specs/2026-07-02-payroll-run-operations-enhancement-ROADMAP.md`. Folds 8 requested requirement areas into the [[Financial Engine Redesign Master|decomposition]], flagging what 2D already built.
> Up: [[Specs Index]] · Roadmap: [[ROADMAP]]

Legend: ✅ done · 🟡 partial · 🔵 new.

| Area | Requirement | Maps to | Status |
|---|---|---|---|
| 1 | Printable **payslip per employee** (preview/print/download/store) | Sub-project 4 | 🔵 |
| 2 | **Amendment after approval** via void/amend/reissue (`Voided`/`Amending`/`Reissued`, ledger nets via counter-entries) | Sub-project 6 (new) | 🔵 |
| 3 | **Add additions/deductions from the run page** (quick actions; closes bug #4 — creating against an approved period must be blocked or routed to an amendment) | Sub-project 3 | 🟡 |
| 4 | **Attendance daily penalty actions** (calculate absence/late/shortage · overtime, target-month selector) | [[Subproject 2E Attendance Daily Overtime Excuse|2E]] | 🟡 |
| 5 | **Attendance/Payroll record contract** — every impact a `PayrollTransaction`: Absence/Late/Shortage→Deduction ✅ (2D); Overtime→Addition 🔵; approved excuse→cancels before posting 🔵 | 2E | 🟡 |
| 6 | **Payroll exports** (Excel/PDF now; CSV/TXT later; bank/IBAN gated; `PayrollExportJob` + exporter registry) | Sub-project 5 | 🔵 |
| 7 | **Permissions** — register new perms in deny-wins [[Access Management]], seed + grant, UI-gate | folded per sub-project | 🔵 |
| 8 | **Audit** — new **origin/source-screen** field on transaction create (`RunPage`/`AttendanceDaily`/`DeductionsPage`/`AdditionsPage`/`Import`); who voided/reissued + old↔new linkage | folded per sub-project | 🔵/🟡 |

## Revised build order
**2E → 3 → 4 → 5 → 6** — records/attendance completeness first, then run-details surface, payslips, exports, and the heaviest lifecycle change (void/amend/reissue) last. Permissions + audit-origin land inside whichever sub-project introduces the action.

## Related
[[Payroll Engine]] · [[Subproject 2E Attendance Daily Overtime Excuse]] · [[Attendance Payroll Impact]] · [[ROADMAP]]
