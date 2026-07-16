---
title: Gusto
aliases: [Gusto Payroll, Gusto HR, ZenPayroll]
tags: [competitor, modern]
status: initial-research
updated: 2026-07-03
---

# Gusto
> The gold standard for delightful US SMB payroll onboarding and run UX — but US-only, with no Saudi/GCC statutory footprint.
> Up: [[Competitor Index]] · System: [[COMPETITORS]]

## Overview
- **Company:** Gusto, Inc. (San Francisco, US) — founded 2011 as ZenPayroll, rebranded Gusto 2015. Well-funded, late-stage private.
- **Product:** All-in-one payroll, benefits, and HR platform for small businesses; payroll is the anchor with benefits administration, hiring/onboarding, time tracking, and light HR wrapped around it.
- **Target Market:** US small businesses (roughly 1–100 employees, sweet spot under ~50), plus accountants/bookkeepers via Gusto's partner program.
- **Pricing:** Tiered SaaS — base monthly fee + per-employee-per-month; tiers commonly Simple / Plus / Premium, plus a contractor-only plan. 🔄 verify current tier names and per-seat pricing.
- **Strengths:** Best-in-class payroll onboarding and run experience; automatic federal/state/local tax filing; strong benefits marketplace; friendly, human tone; excellent contractor and 1099 handling.
- **Weaknesses:** US-only (no international payroll, no GCC/KSA compliance); shallow on mid-market HR depth (performance, complex org, deep reporting); limited configurability for non-US workflows; not built for RTL/Arabic.
- **Positioning:** "Payroll that small businesses actually enjoy running." Product-led, delight-first, accountant-friendly.

## Modules
| Module | Rating (★☆) | Notes |
|---|---|---|
| Payroll | ★★★★★ | The crown jewel — auto tax filing (fed/state/local), unlimited runs, AutoPilot auto-run, corrections, off-cycle, garnishments. US-only. Study for [[Payroll Engine]] run UX, not for statutory breadth. |
| Attendance | ★★★☆☆ | Built-in time tracking + PTO in higher tiers; adequate for SMB, not full WFM. Cf. [[Attendance]]. |
| Employees | ★★★★☆ | Clean employee profiles, self-onboarding, I-9/W-4 e-collection. Cf. [[Employees]]. |
| Recruitment | ★★☆☆☆ | Light hiring/offer + onboarding checklists; not a real ATS. (SanadHR: [[ROADMAP]].) |
| Performance | ★★☆☆☆ | Basic reviews in Premium; not a strength. (SanadHR: [[ROADMAP]].) |
| Workflow | ★★☆☆☆ | Guided flows and reminders rather than a configurable engine. Contrast [[Workflow Engine]]. |
| Approvals | ★★★☆☆ | Simple approvals (time off, hours); not multi-stage no-code chains. Cf. [[Request Center]]. |
| Reports | ★★★☆☆ | Solid payroll/tax reports + custom reports; not deep BI. Cf. [[Reports]]. |
| Dashboards | ★★★☆☆ | Friendly home overview; not a builder. Cf. [[Dashboards]]. |
| ESS | ★★★★★ | Employee self-onboarding + Gusto Wallet is a highlight of the product. Cf. [[ESS]]. |
| Mobile | ★★★★☆ | Gusto Wallet app well regarded for employees (pay, PTO, spending); admin mobile lighter. |
| Documents | ★★★☆☆ | Doc storage, e-sign for onboarding forms; not a template designer. Cf. [[Documents]]. |
| Loans/Expenses | ★★★☆☆ | Reimbursements in payroll; Gusto Wallet cash-advance features; no employer loan ledger like ours. Cf. [[Loans]] / [[Expenses]]. |
| Integrations | ★★★★☆ | Good SMB ecosystem — accounting (QuickBooks/Xero), time, benefits; API/partner program. |
| AI/Analytics | ★★★☆☆ | AI assist / support and some smart suggestions rolling out 🔄 verify scope. |
| Permissions | ★★★☆☆ | Role-based admin/manager/employee; simple, not granular enterprise RBAC. Cf. [[Access Management]]. |
| Organization | ★★☆☆☆ | Departments/managers; no position management or deep org modeling. Cf. [[Org Structure]]. |
| Master Data/Config | ★★☆☆☆ | Opinionated defaults over configurability — great for speed, weak for local rules. Contrast [[Master Data Engine]] / [[Configuration over Hardcoding]]. |

**Saudi/GCC gaps:** No GOSI, no WPS/Mudad bank files, no Qiwa, no Article 84/85 [[End of Service]], no Hijri calendar, no Arabic/RTL. Entirely US tax/compliance-scoped — irrelevant to KSA except as a UX study.

## UX Notes
- **Navigation:** Clean, minimal left/top nav; low cognitive load; onboarding is a guided checklist.
- **Search:** Present but modest; product is simple enough to not lean on search.
- **Dashboard:** Warm, human home with "what needs your attention" framing — a model for empty/next-action states.
- **Configuration:** Deliberately opinionated; fast setup at the cost of flexibility.
- **Automation:** AutoPilot payroll auto-run + reminders; delightful but shallow vs a true engine.
- **Performance:** Snappy, responsive SMB-scale app.
- **Accessibility:** Consumer-grade polish; reasonable a11y 🔄 verify formal conformance.
- **Dark Mode:** Not a signature feature 🔄 verify.
- **Arabic Support:** None — English/US only, LTR. Contrast [[Arabic RTL]].
- **Mobile UX:** Gusto Wallet is a genuinely loved employee app; admin/run-payroll leans web.

## Things we love
- The payroll run flow: clear pre-run summary, "review and submit," obvious what's being paid and filed.
- Employee self-onboarding that offloads data entry to the new hire.
- Human, reassuring copy that de-stresses a scary task (paying people, taxes).

## Things we hate
- US-only — zero applicability to statutory Saudi/GCC needs.
- Shallow configurability; you get Gusto's opinion, not your process.
- Thin mid-market HR (org, performance, deep reporting).

## Customer complaints
Recurring review themes (G2/Trustpilot-style): support wait times and inconsistent resolution as they've scaled; occasional tax-filing/notice hiccups that are painful for SMBs; pricing creep as add-ons stack; limited for growing companies that outgrow the SMB feature ceiling; sparse advanced reporting. (Themes only — no invented quotes/numbers.)

## Feature requests
International/multi-country payroll; deeper HR (performance, org, custom fields); more configurable approval workflows; stronger reporting/BI; better admin mobile; more granular permissions.

## Release Notes
Continued expansion into embedded payroll (Gusto Embedded), accountant tooling, Gusto Wallet financial features, and AI-assisted support/setup 🔄 verify latest specifics.

## Screenshots
- The "Run payroll" review-and-submit summary screen (signature UX).
- Employee self-onboarding checklist.
- Gusto Wallet employee app (pay, PTO, spending).
- Human-toned home dashboard with next-action cards.

## Workflows
- Payroll: Add/onboard employee → hours/PTO flow in → run payroll (or AutoPilot) → auto tax filing + direct deposit. Study for [[Payroll Engine]] run UX.
- Attendance/Leave: Time tracking + PTO accrual feeding hours into a run. Cf. [[Attendance]].
- Recruitment: Light offer + onboarding checklist → self-onboard. (SanadHR: [[ROADMAP]].)
- Approvals: Simple time-off / hours approvals. Cf. [[Request Center]].
- Reports: Payroll, tax, and custom SMB reports. Cf. [[Reports]].

## Ideas worth stealing
- The pre-run "review payroll" summary as a confidence-building moment — mirror it over our [[Immutable Ledger]] with a reproducible, explainable preview.
- Employee-driven self-onboarding to cut HR data entry — feed [[Employees]] and [[ESS]].
- Human, reassuring microcopy on high-stakes finance actions.
- AutoPilot-style scheduled auto-run for recurring, unchanged payroll cycles.

## Improvements we can make
- **Simpler:** Match Gusto's run-payroll clarity, but for KSA — one calm screen that explains GOSI, WPS, and [[End of Service]] impacts.
- **Faster:** Instant tenant bootstrap + self-onboarding like Gusto, without losing config.
- **More configurable:** Gusto's opinion is fixed; our [[Master Data Engine]] + [[Configuration over Hardcoding]] adapt to any Saudi employer's rules.
- **More automated:** AutoPilot-style scheduling on our [[Workflow Engine]], with approvals.
- **More scalable:** [[Immutable Ledger]] makes every run reproducible/auditable — beyond SMB payroll.
- **More beautiful:** Keep Gusto's warmth, deliver it RTL-first via [[Arabic RTL]] and [[Design System]].

## Benchmark
| Product | Rating |
|---|---|
| Gusto | ★★★★★ (US payroll UX) |
| [[ADP Workforce Now\|ADP]] | ★★★☆☆ |
| [[BambooHR]] | ★★★☆☆ (payroll weaker) |
| **SanadHR (Our Design)** | ★★★★★★ |

Gusto sets the bar for how *pleasant* payroll can feel, but it stops at the US border. SanadHR aims to match that run-UX delight while adding what Gusto structurally cannot: native Saudi statutory depth (GOSI/WPS/Mudad/Qiwa, [[End of Service]] Articles 84/85), a reproducible [[Immutable Ledger]] on the [[Financial Calculation Engine]], no-code [[Configuration over Hardcoding]], and first-class [[Arabic RTL]].

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Payroll Engine]] · [[Financial Calculation Engine]] · [[Immutable Ledger]] · [[ESS]] · [[Arabic RTL]] · [[Factorial HR]] · [[Personio]]
