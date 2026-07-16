---
title: PROJECT_STATUS
aliases: [Project Status, Current State, Status]
tags: [control, status]
updated: 2026-07-03
---

# 📊 PROJECT_STATUS — Current Truth

> Last reviewed: **2026-07-03**. The honest state of SanadHR right now.
> Up: [[Home]] · Detail: [[IMPLEMENTATION_STATUS]] · Next: [[ROADMAP]] · Infra: [[Deployment and Infrastructure]]

---

## Overall completion — **~70%**

Active development, **production deployed (early access)**. Backend architecture and core engines are mature and deployed. The [[Payroll Engine]] is under active enhancement. The frontend is mid-transition from mock data to live API consumption.

| Layer | Status | Est. |
|---|---|---|
| Backend architecture & modules | Deployed, active dev | ~80% |
| [[Payroll Engine]] / [[Financial Calculation Engine]] | Active enhancement | ~70% |
| Database schema & migrations | Provisioned, applied | ~85% |
| Frontend (UI shell + builders) | Built | ~75% |
| Frontend live-API integration | In transition | ~45% |
| Infrastructure / DevOps | Deployed | ~80% |

---

## Deployed status

| Component | Status | Location |
|---|---|---|
| Frontend | Live | Vercel (Next.js) |
| Backend API | Live & verified | Azure App Service — `hrcloud-api-v4xd.azurewebsites.net` |
| Database | Provisioned, migrated | Azure PostgreSQL Flexible Server (UAE North) |
| Swagger | Enabled | `/swagger` (Development mode) |

- CORS configured for Vercel domains. `InitialCreate` migration verified applied **2026-06-09**.
- Cross-region API (West Europe) ↔ DB (UAE North) latency ~40ms. Full detail: [[Deployment and Infrastructure]].

---

## Completed / stable

- **[[Employees|Core HR]]** — employee lifecycle, org structure, multi-company.
- **[[Identity]]** — JWT auth, RBAC, [[Access Management]].
- **[[Tenancy|Multi-tenant isolation]]**.
- **[[Workflows]]** — dynamic builder + state-machine engine.
- **[[Documents]]** — template engine, RTL PDF generation.
- **[[Dashboards]] / [[Reports]]** — builders with widgets, filters, PDF/XLSX export.
- **[[ESS]]** — self-service portal + [[Request Center]].
- **[[Notifications]]** — engine (in-app + email).
- **[[Financial Calculation Engine]]** — ledger, rule engine, run state machine, batch execution (Passes 1–4).
- **Payroll operational layer** — [[Payroll Types Scope Cutoff|types/scope/cutoff]], [[Payroll Additions and Deductions|additions/deductions]], [[Attendance Payroll Impact|attendance→deduction sync]].

## In progress

- **Payroll enhancements** — [[Subproject 2E Attendance Daily Overtime Excuse|overtime→addition, configurable rates, excuse cancel, daily actions]].
- **Frontend live-API migration** — [[Tasks]] module remains mock-only (`tasks-mock-data.ts`).
- **[[Loans]] & [[Expenses]]** payroll integration.

---

## Known blockers / risks

| Item | Impact | Notes |
|---|---|---|
| Redis not provisioned | Medium | Caching infra ready, not live. |
| Free-tier hosting (App Service F1) | Medium | Cold starts, throttling risk under load. |
| Mock → live data gap | High | [[Tasks]] screens still on mock data. |
| Dev mode in production | Low/Med | Swagger exposed; switch to Production profile before GA. |
| Cross-region DB latency | Low | ~40ms; consider co-location. |

## Next recommended work

1. Finish [[Payroll Engine]] enhancements ([[Subproject 2E Attendance Daily Overtime Excuse|2E]] → run details → payslips → exports; see [[Payroll Run Operations Roadmap]]).
2. Complete frontend live-API integration — eliminate remaining mock data.
3. Provision Redis; co-locate API + DB; harden for GA.
4. Expand test coverage on payroll reproducibility + tenant isolation.

> Per-module build matrix: [[IMPLEMENTATION_STATUS]]. Feature roadmap: [[ROADMAP]].
