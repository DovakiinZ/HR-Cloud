---
title: HiBob
aliases: [HiBob, Bob]
tags: [competitor, modern]
status: initial-research
updated: 2026-07-03
---

# HiBob
> "Bob" — culture-first, modern mid-market HR platform with beautiful UX, flexible fields, and strong engagement.
> Up: [[Competitor Index]] · System: [[COMPETITORS]]

## Overview
- **Company:** HiBob (UK/Israel HQ; founded 2015; well-funded, valuation reported ~$2.45B in a 2024 round, 🔄 verify). - **Product:** "Bob" — modern HRIS focused on culture/engagement, org agility, people analytics, plus time/attendance, compensation, and (via partners) payroll connectors. - **Target Market:** Mid-market, modern/global-minded companies (~50–1,500 employees), tech and services led. - **Pricing:** Per-employee-per-month, module-based, quote-driven (🔄 verify).
- **Strengths:** Standout modern UX; highly flexible custom fields/objects and org structures; strong engagement (kudos, shoutouts, surveys, club/homepage feed); good people analytics; global-minded (multi-entity, multi-currency-friendly). - **Weaknesses:** **No native payroll** — relies on payroll partners/connectors (a real gap); depth of statutory compliance is via partners, not owned; **no Saudi/GCC statutory engine** (no GOSI/WPS/Mudad/Qiwa/Nitaqat/EOS Article 84/85, no Arabic RTL first-class). - **Positioning:** "The modern HR platform for the way people work today" — culture and flexibility as the wedge.

## Modules
| Module | Rating | Notes |
|---|---|---|
| Payroll | ★★☆☆☆ | **No native payroll** — payroll hub/connectors to 3rd parties. KSA statutory not owned. |
| Attendance | ★★★★☆ | Time & attendance + scheduling module; clean, modern. |
| Employees | ★★★★★ | Flexible people records + custom fields; very configurable. Benchmark for [[Employees]]. |
| Recruitment | ★★★☆☆ | ATS via Bob Hiring / integrations; not a best-of-breed. |
| Performance | ★★★★★ | Reviews, goals, 1:1s, talent — genuinely strong culture/performance. |
| Workflow | ★★★★☆ | Task-lists + lifecycle workflows; not a Rippling-grade no-code studio. |
| Approvals | ★★★★☆ | Configurable approval flows, decent conditions. |
| Reports | ★★★★☆ | Good people analytics + dashboards. |
| Dashboards | ★★★★★ | Beautiful, engagement + analytics dashboards — a UX high bar. |
| ESS | ★★★★★ | Homepage feed, shoutouts, self-service — engagement-led ESS. Benchmark for [[ESS]]. |
| Mobile | ★★★★☆ | Well-designed mobile app; social/engagement forward. No Arabic RTL. |
| Documents | ★★★★☆ | Docs, e-sign, tasks tied to lifecycle. |
| Loans/Expenses | ★★☆☆☆ | Minimal; not a spend/loan platform. |
| Integrations | ★★★★☆ | Marketplace + payroll connectors + API. |
| AI/Analytics | ★★★★☆ | "Bob" AI assist + people analytics; maturing. |
| Permissions | ★★★★☆ | Flexible, group/attribute-influenced access. |
| Organization | ★★★★★ | Dynamic org charts, sites, departments — org agility is a strength. |
| Master Data/Config | ★★★★★ | Highly flexible fields/objects/lists — strong [[Master Data Engine]] / [[Configuration over Hardcoding]] model to study. |
| **KSA/GCC compliance** | ★☆☆☆☆ | **None native.** No GOSI/WPS/Mudad/Qiwa/Nitaqat/EOS, no Arabic RTL. SanadHR's opening. |

## UX Notes
- **Navigation:** modern, friendly, engagement-forward home. **Search:** people/org search. **Dashboard:** the best-looking in this set — analytics + culture feed. **Configuration:** flexible fields/objects without code — a highlight. **Automation:** lifecycle task flows; lighter than [[Rippling]]. **Performance:** polished SPA. **Accessibility:** good. **Dark Mode:** 🔄 verify. **Arabic Support:** not first-class RTL — clear gap. **Mobile UX:** among the best; social/engagement led.

## Things we love
- **Beautiful, modern UX** and a culture/engagement homepage feed (shoutouts, kudos, surveys) — the emotional layer most HRIS ignore.
- **Flexible custom fields/objects/lists** without code — strong config model for [[Master Data Engine]] + [[Configuration over Hardcoding]].
- Dynamic org structures (sites/departments/dynamic groups) — great for [[Org Structure]].
- People analytics that feel like a product, not a bolt-on — bar for [[Dashboards]].

## Things we hate
- **No native payroll** — the biggest structural gap; statutory depth is outsourced to partners.
- Nothing for KSA/GCC statutory reality or Arabic RTL.
- Automation is lighter than Rippling; not a cross-app workflow studio.

## Customer complaints
- Payroll dependency on partners causes integration friction (🔄 verify).
- Reporting/customization limits at the edges for complex needs.
- Pricing/module bundling opacity.

## Feature requests
- Native payroll / deeper statutory compliance.
- Stronger automation/workflow.
- More advanced analytics/report builder.

## Release Notes
- Ongoing engagement, talent, compensation, "Bob" AI, and analytics enhancements (🔄 verify 2025–2026 specifics).

## Screenshots
- 🔄 verify — capture homepage feed, org chart, people analytics, custom-fields config from hibob.com.

## Workflows
- **Payroll:** no native run — data pushed to a payroll partner/connector. - **Attendance/Leave:** modern time-off request → approval → accrual, feeds analytics. - **Recruitment:** Bob Hiring / integrated ATS → onboarding lifecycle tasks. - **Approvals:** configurable lifecycle approval flows. - **Reports:** people analytics dashboards, engagement + headcount.

## Ideas worth stealing
- **Culture/engagement layer** (homepage feed, shoutouts, surveys) as a first-class product surface — feeds [[ESS]], [[Notifications]], [[Dashboards]]. Most Saudi HR tools are cold/transactional; this is a differentiator we can localize.
- **Flexible custom fields/objects/lists without code** — validates our [[Master Data Engine]] + [[Configuration over Hardcoding]] bet; steal the UX.
- Dynamic org structures for [[Org Structure]].
- People-analytics-as-product polish for [[Dashboards]].

## Improvements we can make
- **Simpler:** Bob needs a payroll partner; SanadHR ships native KSA payroll on a [[Financial Calculation Engine]] — one system, not two.
- **Faster:** native GOSI/WPS/Mudad/EOS vs Bob's connector round-trips.
- **More configurable:** match Bob's flexible fields via [[Master Data Engine]] while owning statutory [[Rule Engine]] logic.
- **More automated:** [[Workflow Engine]] + [[Completion Effects Engine]].
- **More scalable:** [[Multi-Tenancy]] for KSA/GCC.
- **More beautiful:** match Bob's UX warmth but [[Arabic RTL]]-first with our [[Design System]].

## Benchmark
| Product | Rating | Why |
|---|---|---|
| HiBob | ★★★★☆ | Best modern UX + culture/engagement, but no native payroll. |
| [[BambooHR]] | ★★★★☆ | Simpler SMB, less flexible, US-only payroll. |
| [[Rippling]] | ★★★★★ | Deeper automation/unification (less culture-warm). |
| **SanadHR (Our Design)** | ★★★★★★ | Bob-grade UX/flexibility/culture PLUS native, auditable KSA payroll (GOSI/WPS/Mudad/EOS) on an [[Immutable Ledger]] — beautiful AND statutorily real, which Bob is not. |

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[ESS]] · [[Employees]] · [[Master Data Engine]] · [[Configuration over Hardcoding]] · [[Org Structure]] · [[Dashboards]] · [[Notifications]] · [[Arabic RTL]] · [[BambooHR]] · [[Rippling]] · [[Deel]]
