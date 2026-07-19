---
title: Jisr
aliases: [Jisr HR, Jisr Payroll, جسر]
tags: [competitor, regional]
status: initial-research
updated: 2026-07-03
---

# Jisr

> The KSA HR + payroll leader — deepest local Saudi compliance among the regional SMB→mid players.
> Up: [[Competitor Index]] · System: [[COMPETITORS]]

## Overview
- **Company:** Jisr (formerly PaySky/PennyPipes-era rebranded; Riyadh-headquartered Saudi HR-tech firm). One of the most funded and best-known Saudi-origin HR SaaS vendors. 🔄 verify funding/headcount specifics.
- **Product:** All-in-one cloud HR + payroll platform: core HR, payroll, attendance/time, self-service, plus deep government-integration modules (GOSI, WPS, Mudad, Qiwa, Absher/Muqeem-adjacent flows).
- **Target Market:** Saudi SMB and mid-market; strong pull with local companies that must satisfy MOL/Qiwa/GOSI mandates. Arabic-first customer base.
- **Pricing:** Per-employee-per-month tiered SaaS, with module add-ons; commonly positioned as affordable for SMB. 🔄 verify exact tiers/PEPM.
- **Strengths:** Best-in-class Saudi compliance depth; native GOSI/WPS/Mudad/Qiwa wiring; Arabic-first UX; localized End-of-Service under Labor Law; strong local brand + support presence in KSA.
- **Weaknesses:** Payroll calculation is largely a black box (hard to trace/reproduce line items); configurability is template-bound rather than truly no-code; UX is competent but not best-in-class; reporting/analytics shallower than enterprise suites; heavy KSA focus limits multi-country breadth.
- **Positioning:** "The Saudi payroll-and-compliance engine you can trust to stay legal." Compliance is the moat, not UX or extensibility.

## Modules
| Module | Rating (★☆) | Notes |
|---|---|---|
| [[Payroll Engine\|Payroll]] | ★★★★☆ | Strong Saudi payroll; GOSI contributions, allowances/deductions, WPS SIF file generation. Calculation traceability is weak — no [[Immutable Ledger\|immutable ledger]]. |
| [[Attendance]] | ★★★★☆ | Biometric/geofence check-in, shifts, overtime; feeds payroll. See [[Attendance Payroll Impact]]. |
| [[Employees]] | ★★★★☆ | Solid employee master with Iqama/GOSI IDs, documents, contracts. |
| Recruitment | ★★☆☆ | Basic ATS; not a differentiator. [[ROADMAP]] |
| Performance | ★★☆☆ | Light appraisal/goals; not deep. [[ROADMAP]] |
| [[Workflows]] | ★★★☆ | Approval routing for leave/requests; configuration is menu-driven, not a visual [[Workflow Engine\|builder]]. |
| Approvals | ★★★☆ | Multi-level approvals present; limited conditional logic. |
| [[Reports]] | ★★★☆ | Standard statutory + operational reports; custom reporting limited. |
| [[Dashboards]] | ★★★☆ | Functional HR dashboards; not deeply drill-downable. |
| [[ESS]] | ★★★★☆ | Strong employee/manager self-service; payslips, leave, requests. |
| Mobile | ★★★★☆ | Well-adopted mobile app for check-in + ESS. |
| [[Documents]] | ★★★☆ | Contract/letter generation; Arabic templates. |
| [[Loans]]/[[Expenses]] | ★★★☆ | Loans/advances handled in payroll; expenses lighter. |
| Integrations | ★★★★☆ | Deep GOV integrations are the crown jewel; fewer generic 3rd-party connectors. |
| AI/Analytics | ★★☆☆ | Emerging; not a strength. [[ROADMAP]] |
| [[Access Management\|Permissions]] | ★★★☆ | Role-based access; granularity is decent, not fully matrixed. |
| [[Org Structure\|Organization]] | ★★★☆ | Departments/branches; org chart present. |
| [[Master Data Engine\|Master Data/Config]] | ★★★☆ | Configurable catalogs but bounded; not [[Configuration over Hardcoding\|no-code end-to-end]]. |

**Saudi compliance:** GOSI ✅ (contribution calc + registration flows), WPS ✅ (SIF generation, bank/Mudad routing), Mudad ✅, Qiwa ✅ (contracts/authentication flows), **End-of-Service** ✅ under Labor Law Arts. 84/85 — a core selling point. See [[Saudi Compliance Notes]].

## UX Notes
- **Navigation:** Clean, module-oriented left nav; familiar HRIS layout.
- **Search:** Adequate; not command-palette fast.
- **Dashboard:** Role dashboards, KPI tiles; utilitarian.
- **Configuration:** Settings-driven; admins configure within provided structures rather than building freely.
- **Automation:** Rule-based approvals; limited event automation.
- **Performance:** Generally responsive web app.
- **Accessibility:** Standard.
- **Dark Mode:** 🔄 verify.
- **Arabic Support:** Strong — Arabic-first product with proper RTL across core flows (their home-market advantage). See [[Arabic RTL]].
- **Mobile UX:** Mature app, high adoption for attendance + ESS.

## Things we love
- Compliance is treated as a first-class product surface, not an afterthought.
- Government integrations (GOSI/WPS/Mudad/Qiwa) are genuinely deep and trusted.
- Arabic-first from the ground up.

## Things we hate
- Payroll math is opaque — no reproducible, auditable line-item ledger.
- Configurability is bounded by templates; power users hit walls.
- Reporting/analytics don't match the compliance depth.

## Customer complaints
Recurring community themes: support responsiveness varies with growth; occasional friction/lag during payroll runs and month-end; UI feels dated to users coming from modern SaaS; edge-case payroll adjustments require support intervention; report customization limited. 🔄 verify current sentiment (no invented quotes/numbers).

## Feature requests
More flexible/custom reporting; clearer payroll breakdowns employees can self-explain; deeper performance/recruitment; more open API/integration surface; richer analytics. 🔄 verify.

## Release Notes
Direction: continued deepening of KSA gov integrations, mobile, and payroll automation; incremental analytics/AI. 🔄 verify recent specifics.

## Screenshots
Capture later: Arabic-first payroll run screen; WPS/SIF export flow; GOSI contribution breakdown; mobile check-in; ESS payslip.

## Workflows
- **Payroll (+ WPS/GOSI):** Configure salary structure → run payroll → GOSI contributions computed → generate WPS SIF → route via Mudad/bank. Strong but not line-item-traceable.
- **Attendance/Leave:** Biometric/geofence capture → shift rules → overtime → payroll feed → leave balances.
- **Recruitment:** Basic requisition → candidate → hire → onboard.
- **Approvals:** Multi-level menu-configured approvals.
- **Reports:** Statutory + operational; custom limited.

## Ideas worth stealing
- Treat Saudi compliance (GOSI/WPS/Mudad/Qiwa/EOS) as headline product surfaces, not buried settings.
- Deep, trusted government-integration flows as the primary trust anchor for KSA buyers.

## Improvements we can make
- **Simpler:** Explain payroll to a non-accountant with a transparent breakdown.
- **Faster:** Modern app-shell speed vs their utilitarian feel.
- **More configurable:** True [[Configuration over Hardcoding\|no-code]] via [[Master Data Engine]] vs bounded templates.
- **More automated:** Visual [[Workflow Engine]] vs menu-configured approvals.
- **More scalable:** [[Financial Calculation Engine]] where payroll is one app on a general ledger/rule engine.
- **More beautiful:** [[Design System]]-driven, Linear/HubSpot-grade UX vs dated HRIS look.

## Benchmark
| Product | Rating |
|---|---|
| Jisr | ★★★★☆ |
| [[ZenHR]] | ★★★★☆ |
| [[PalmHR]] | ★★★☆ |
| **SanadHR (Our Design)** | ★★★★★★ |

Jisr wins on compliance depth but loses on payroll transparency and configurability. SanadHR matches its Saudi compliance while adding a **reproducible [[Immutable Ledger\|immutable-ledger]] payroll** and **no-code configurability** Jisr structurally cannot offer without re-architecting.

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Financial Calculation Engine]] · [[Payroll Engine]] · [[Saudi Compliance Notes]] · [[End of Service]] · [[Arabic RTL]] · [[Master Data Engine]] · [[ZenHR]] · [[PalmHR]]
