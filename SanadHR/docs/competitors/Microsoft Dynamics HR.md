---
title: Microsoft Dynamics HR
aliases: [Dynamics 365 HR, D365 HR, Dynamics 365 Human Resources, MS Dynamics HR]
tags: [competitor, enterprise]
status: initial-research
updated: 2026-07-03
---

# Microsoft Dynamics HR

> The M365-ecosystem HR play — core HR folded into Dynamics 365 Finance & Operations, strongest when you already live in Microsoft (Teams, Power Platform, Entra).
> Up: [[Competitor Index]] · System: [[COMPETITORS]]

## Overview
- **Company:** Microsoft (Redmond, WA).
- **Product:** Dynamics 365 Human Resources — core HR, benefits, leave & absence, compensation, org management. Now merged into the Dynamics 365 Finance & Operations (F&O) app; leans on Power Platform, Dataverse, Teams, and Viva for experience. No native payroll — relies on partners/ISVs.
- **Target Market:** Microsoft-centric enterprises and mid-market already on D365 F&O / M365.
- **Pricing:** Per-user subscription within the D365 family; strongest value when bundled with existing Microsoft agreements 🔄 verify current SKUs.
- **Strengths:** Deep M365/Teams/Power Platform/Entra ID integration, Dataverse extensibility, Power BI analytics, familiar Microsoft UX, low-code customization via Power Apps.
- **Weaknesses:** No native payroll (partner-dependent), thinner talent/recruiting/performance vs SAP/Oracle/Workday, product has been de-emphasized/merged into F&O, needs ISVs for full HCM breadth, Saudi compliance via partners.
- **Positioning:** "HR for the Microsoft estate" — an extensible core HR you build on with Power Platform, not a turnkey global HCM suite.

## Modules
| Module | Rating (★☆) | Notes |
|---|---|---|
| Payroll | ★★☆☆☆ | No native payroll; via partners/ISVs (e.g. regional payroll on Dynamics). Big gap vs [[Payroll Engine]] / [[Financial Calculation Engine]]. |
| Attendance | ★★★☆☆ | Leave & absence native; time/attendance often via ISV or Shifts/Teams. Cf. [[Attendance]]. |
| Employees | ★★★★☆ | Solid core HR / worker records on Dataverse. Cf. [[Employees]]. |
| Recruitment | ★★☆☆☆ | Attract was retired; recruiting typically via LinkedIn/partners. (SanadHR: [[ROADMAP]].) |
| Performance | ★★★☆☆ | Basic performance/goals; Viva Goals adjacent. (SanadHR: [[ROADMAP]].) |
| Workflow | ★★★★☆ | Power Automate flows are the automation backbone — powerful, low-code. Cf. [[Workflow Engine]]. |
| Approvals | ★★★★☆ | Approvals via Power Automate + Teams approvals. Cf. [[Request Center]]. |
| Reports | ★★★★☆ | Power BI is a real strength. Cf. [[Reports]]. |
| Dashboards | ★★★★☆ | Power BI + workspaces. Cf. [[Dashboards]]. |
| ESS | ★★★☆☆ | ESS via Employee self-service + Teams app; adequate. Cf. [[ESS]]. |
| Mobile | ★★★☆☆ | Via Teams/Power Apps mobile; not a dedicated best-in-class HR app. |
| Documents | ★★★☆☆ | Docs via SharePoint/Dataverse; not a design-forward builder. Cf. [[Document Platform]]. |
| Loans/Expenses | ★★★☆☆ | Expenses in D365 Finance; loans via payroll ISV. Cf. [[Loans]] / [[Expenses]]. |
| Integrations | ★★★★★ | Dataverse + Power Platform + M365 + Entra — top-tier within the Microsoft world. |
| AI/Analytics | ★★★★☆ | Copilot across Dynamics/M365 + Power BI 🔄 verify HR-specific Copilot scope. |
| Permissions | ★★★★☆ | Entra ID + D365 security roles; enterprise-grade. Cf. [[Access Management]]. |
| Organization | ★★★★☆ | Position/org management in F&O. Cf. [[Org Structure]]. |
| Master Data/Config | ★★★★☆ | Dataverse tables + Power Apps make it very extensible/low-code. Cf. [[Master Data Engine]] / [[Configuration over Hardcoding]]. |

## UX Notes
- **Navigation:** Familiar D365 F&O shell; functional, form-dense, enterprise ERP feel.
- **Search:** Standard D365 search; Copilot adds natural-language help.
- **Dashboard:** Power BI-driven workspaces.
- **Configuration:** Power Apps/Dataverse low-code is the differentiator — build what's missing.
- **Automation:** Power Automate is the star — broad, low-code, connector-rich.
- **Performance:** Solid Azure cloud performance.
- **Accessibility:** Strong Microsoft a11y standards.
- **Dark Mode:** Available in parts of the Microsoft UI 🔄 verify D365 HR specifics.
- **Arabic Support:** Arabic/RTL supported across Dynamics/M365; quality decent but Saudi HR compliance still partner-driven. Contrast [[Arabic RTL]].
- **Mobile UX:** Delivered through Teams/Power Apps rather than a purpose-built HR app.

## Things we love
- Power Automate + Power Apps low-code extensibility on Dataverse.
- Native Teams/Viva/Entra integration for the Microsoft estate.
- Power BI analytics out of the box.

## Things we hate
- No native payroll — a fundamental HCM gap for Saudi/GCC.
- Product strategy churn (Attract/Onboard retired; HR merged into F&O).
- Requires ISVs/Power Platform work to reach full HCM breadth.

## Customer complaints
Recurring themes (G2/TrustRadius/Reddit r/dynamics365): lacks native payroll and full talent breadth; strategy/merge confusion; needs Power Platform/ISV investment to complete; ERP-flavored UX; setup complexity for full HR scenarios. (Themes only.)

## Feature requests
Native payroll (esp. regional/Saudi); clearer standalone HR roadmap after the F&O merge; richer talent/recruiting/performance; a dedicated modern HR mobile app; less reliance on ISVs.

## Release Notes
Continued Copilot rollout across Dynamics/M365, ongoing consolidation into D365 F&O, Power Platform + Dataverse investment, Viva alignment 🔄 verify latest wave specifics.

## Screenshots
- D365 F&O worker record + org management.
- Power Automate approval flow builder.
- Power BI HR analytics workspace.
- Teams-embedded ESS/approvals.

## Workflows
- Payroll: partner/ISV payroll integrated via Dataverse. Cf. [[Payroll Engine]].
- Attendance/Leave: leave & absence + Teams Shifts → payroll ISV. Cf. [[Attendance Payroll Impact]].
- Recruitment: LinkedIn/partner ATS → hire into worker record.
- Approvals: Power Automate + Teams approvals. Cf. [[Workflow Engine]].
- Reports: Power BI over Dataverse.

## Ideas worth stealing
- Low-code extensibility where admins build missing objects/flows — echoes [[Master Data Engine]] + [[Workflow Engine]].
- Deep chat-surface integration (approvals in the tool people already use) — [[Notifications]].
- Analytics-first dashboards.

## Improvements we can make
- **Simpler:** Turnkey Saudi HR + payroll without stitching Power Platform + ISVs.
- **Faster:** Ready-to-run compliance vs build-it-yourself Dataverse.
- **More configurable:** Native no-code object/rule layer — [[Master Data Engine]] / [[Configuration over Hardcoding]].
- **More automated:** [[Workflow Engine]] + [[Completion Effects Engine]] built into HR, not bolted via Power Automate.
- **More scalable:** Native reproducible payroll finance — [[Financial Calculation Engine]] / [[Immutable Ledger]].
- **More beautiful:** Purpose-built RTL HR UX vs ERP forms — [[Design System]] + [[Arabic RTL]].

## Benchmark
| Product | Rating |
|---|---|
| Microsoft Dynamics HR | ★★★☆☆ |
| [[Workday]] | ★★★★☆ |
| [[SAP SuccessFactors\|SAP SuccessFactors]] | ★★★★☆ |
| **SanadHR (Our Design)** | ★★★★★★ |

Dynamics HR shines only inside the Microsoft estate and needs partners for payroll and full HCM. SanadHR delivers native Saudi payroll on a reproducible [[Financial Calculation Engine]], a built-in no-code [[Master Data Engine]] + [[Workflow Engine]] (no Power Platform assembly required), and a purpose-built RTL [[Design System]] — a complete HR OS, not a construction kit.

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Workflow Engine]] · [[Master Data Engine]] · [[Financial Calculation Engine]] · [[Notifications]] · [[Arabic RTL]] · [[Workday]] · [[Oracle HCM]]
