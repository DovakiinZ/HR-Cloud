---
title: Workday
aliases: [Workday HCM, Workday Human Capital Management]
tags: [competitor, enterprise]
status: initial-research
updated: 2026-07-03
---

# Workday

> The UX benchmark of the enterprise tier — cleanest experience, single in-memory object model, but rigid, expensive, and partner-dependent to configure.
> Up: [[Competitor Index]] · System: [[COMPETITORS]]

## Overview
- **Company:** Workday, Inc. (Pleasanton, CA).
- **Product:** Workday HCM — Core HR, Payroll (limited native country coverage + Global Payroll Connect/partners), Time Tracking, Absence, Recruiting, Talent & Performance, Learning, Compensation, plus Workday Financials and Prism/People Analytics.
- **Target Market:** Large and mid-large enterprises prioritizing UX and analytics; strong in services, tech, healthcare, higher-ed.
- **Pricing:** Premium PEPM subscription, module-based; among the most expensive in the tier; long implementations via certified partners 🔄 verify bands.
- **Strengths:** Best-in-tier UX and mobile, unified in-memory object model, strong analytics/Prism, continuous single-version updates (twice-yearly), strong reporting.
- **Weaknesses:** Cost, rigidity/opinionated model, native payroll only in a few countries (relies on partners/connectors elsewhere incl. Saudi), configuration still needs certified consultants, closed integration model.
- **Positioning:** "The modern enterprise system of record." Bought for experience, analytics, and single-version simplicity.

## Modules
| Module | Rating (★☆) | Notes |
|---|---|---|
| Payroll | ★★★☆☆ | Native payroll only in select countries (US/CA/UK/FR etc.); Saudi/GCC via Global Payroll Connect + partners. Big gap vs [[Payroll Engine]] / [[Financial Calculation Engine]]. |
| Attendance | ★★★★☆ | Time Tracking + Absence clean and well-integrated. Cf. [[Attendance]]. |
| Employees | ★★★★★ | Single object model, Business Process Framework — reference-grade core HR. Cf. [[Employees]] / [[Employee Lifecycle]]. |
| Recruitment | ★★★★☆ | Workday Recruiting solid, unified with core. (SanadHR: [[ROADMAP]].) |
| Performance | ★★★★☆ | Talent & Performance well integrated. (SanadHR: [[ROADMAP]].) |
| Workflow | ★★★★★ | Business Process Framework is a standout — everything is a configurable BP. Cf. [[Workflow Engine]]. |
| Approvals | ★★★★★ | Approvals/conditions modeled inside every business process. Cf. [[Request Center]]. |
| Reports | ★★★★★ | Report writer + calculated fields + Prism; excellent. Cf. [[Reports]]. |
| Dashboards | ★★★★★ | Worklets, dashboards, People Analytics — a strength. Cf. [[Dashboards]]. |
| ESS | ★★★★★ | Clean, consumer-grade self-service. Cf. [[ESS]]. |
| Mobile | ★★★★★ | Best mobile app in the tier. |
| Documents | ★★★☆☆ | Document generation improving; not a rich visual builder. Cf. [[Document Platform]]. |
| Loans/Expenses | ★★★☆☆ | Expenses in Workday Financials (separate SKU); loans via payroll where native. Cf. [[Loans]] / [[Expenses]]. |
| Integrations | ★★★★☆ | Workday Studio/EIB/APIs powerful but a comparatively closed ecosystem. |
| AI/Analytics | ★★★★★ | Prism + Workday AI/ML skills-cloud; analytics leadership 🔄 verify latest AI. |
| Permissions | ★★★★☆ | Security groups/domains robust; admin learning curve. Cf. [[Access Management]]. |
| Organization | ★★★★★ | Supervisory/matrix org structures native and elegant. Cf. [[Org Structure]]. |
| Master Data/Config | ★★★★☆ | Highly configurable via BP + calculated fields, but rigid model boundaries; certified-consultant driven. Cf. [[Master Data Engine]] / [[Configuration over Hardcoding]]. |

## UX Notes
- **Navigation:** Clean, consistent, worklet-based home; the tier's usability leader.
- **Search:** Fast, good global search across objects.
- **Dashboard:** Worklets/dashboards attractive and role-aware.
- **Configuration:** Business Process Framework + calculated fields — powerful, but you work within Workday's opinions.
- **Automation:** BP conditions/sub-processes drive automation elegantly.
- **Performance:** Snappy for the tier thanks to in-memory model.
- **Accessibility:** Strong a11y reputation.
- **Dark Mode:** Limited/not a headline feature 🔄 verify.
- **Arabic Support:** Localization + RTL available but not a first-class focus; Saudi payroll not native. Contrast [[Arabic RTL]].
- **Mobile UX:** Class-leading; near-parity with web for common tasks.

## Things we love
- Business Process Framework — configurable, condition-driven flows for every transaction.
- Consumer-grade UX and mobile.
- Single-version, twice-yearly updates with strong reporting/analytics.

## Things we hate
- Native payroll gaps (Saudi/GCC not native) force partner connectors.
- Rigidity — you conform to Workday's model, not the reverse.
- Premium cost + certified-partner dependence for changes.

## Customer complaints
Recurring themes (Gartner/G2/TrustRadius/Reddit r/workday): expensive; configuration/BP still needs certified consultants; native payroll country coverage limited; integrations feel closed/costly; some admin tasks unexpectedly rigid; report-writer power has a learning curve. (Themes only.)

## Feature requests
Native payroll in more countries (incl. Saudi/GCC); easier self-service configuration without partners; more open integrations; richer document generation; lower cost of ownership.

## Release Notes
Continued Workday AI (skills cloud, generative assistants), Prism analytics expansion, Extend developer platform, and twice-yearly feature releases 🔄 verify latest release specifics.

## Screenshots
- Worklet-based home dashboard.
- Business Process Framework definition (steps/conditions).
- Report writer + calculated fields.
- Mobile app inbox/approvals.

## Workflows
- Payroll: native where supported; elsewhere Global Payroll Connect → partner engine → results back. Cf. [[Payroll Engine]].
- Attendance/Leave: Time Tracking + Absence → payroll input. Cf. [[Attendance Payroll Impact]].
- Recruitment: Recruiting → hire flows into core via BP.
- Approvals: every transaction is a Business Process with conditional routing. Cf. [[Workflow Engine]].
- Reports: custom report writer + Prism datasets.

## Ideas worth stealing
- Everything-is-a-business-process modeling — a north star for [[Workflow Engine]] + [[Request Center]].
- Consumer-grade ESS/mobile polish.
- Calculated fields as a lightweight in-model rule layer (cf. [[Rule Engine]]).

## Improvements we can make
- **Simpler:** Same BP elegance but no-code and partner-free — [[Workflow Engine]].
- **Faster:** Rapid bootstrap; no multi-quarter certified implementation.
- **More configurable:** Break Workday's model rigidity with data-driven objects — [[Master Data Engine]] / [[Configuration over Hardcoding]].
- **More automated:** [[Completion Effects Engine]] chains post-approval effects Workday handles as sub-processes.
- **More scalable:** Native, reproducible Saudi payroll on an [[Immutable Ledger]] — no partner connector.
- **More beautiful:** Match Workday's polish while being RTL-first — [[Design System]] + [[Arabic RTL]].

## Benchmark
| Product | Rating |
|---|---|
| Workday | ★★★★☆ |
| [[SAP SuccessFactors\|SAP SuccessFactors]] | ★★★★☆ |
| [[Oracle HCM\|Oracle HCM]] | ★★★★☆ |
| **SanadHR (Our Design)** | ★★★★★★ |

Workday sets the UX/analytics bar but has no native Saudi payroll and is rigid + premium-priced. SanadHR aims to match its business-process elegance ([[Workflow Engine]]) and polish ([[Design System]]) while owning native Saudi payroll on a reproducible [[Financial Calculation Engine]] / [[Immutable Ledger]] with GOSI/WPS and [[End of Service]] built in — the experience without the payroll gap or the lock-in.

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Workflow Engine]] · [[Request Center]] · [[Financial Calculation Engine]] · [[Immutable Ledger]] · [[Design System]] · [[SAP SuccessFactors]] · [[Oracle HCM]]
