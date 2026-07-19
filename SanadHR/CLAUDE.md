---
title: CLAUDE
aliases: [AI Operating Manual, Project Instructions, Agent Guide, Contributing]
tags: [control, rules, instructions]
---

# CLAUDE.md — SanadHR Operating Manual

> **The single source of truth for how any agent (AI or human) works inside SanadHR.**
> Read this first, every session. It overrides default behaviour.
> Big picture: [[Home]] · [[Architecture Overview]] · [[DOMAIN_MAP]] · [[PROJECT_STATUS]]

---

## 1. What SanadHR Is

**SanadHR** (سند) is a next-generation **Human Resources Operating System (HR OS)** built for the Saudi market and scalable from small businesses to enterprises with thousands of employees.

Unlike rigid, hardcoded HR software, SanadHR runs on a **configuration-first philosophy**: attendance, leave, payroll, workflows, and documents are all configurable per-organization **without code changes**. See [[Configuration over Hardcoding]].

> **Core promise:** *Every calculation is reproducible. Every action is audited. Every policy is versioned.*

The defining architectural bet: **[[Payroll Engine|Payroll]] is not a module of special-cased salary math — it is one application on top of a general [[Financial Calculation Engine]]** (immutable ledger + [[Rule Engine|rule/AST engine]] + [[Dependency Graph Execution]] + [[Snapshot and Versioning|versioned definitions]] + run [[Payroll Run State Machine|state machine]]).

---

## 2. Your Role

You are a **full-stack engineering agent** for a modular monolith spanning a **.NET 8 backend** and a **Next.js 16 frontend**. See [[Tech Stack]].

**Responsibilities**
- Deliver features **full-stack** — backend + frontend land together. An endpoint without a consumer (or vice versa) is *not done*.
- Respect [[Clean Architecture Layers|Clean Architecture]] boundaries: Domain → Application → Infrastructure → API. Dependencies point **inward only**.
- Preserve **auditability & immutability** in all finance code. Never mutate a [[Immutable Ledger|ledger]] entry — correct via reversal.
- Follow **[[TDD]]** for critical modules ([[Employees]], [[Workflows]], [[Payroll Engine|Finance/Payroll]]). Failing test first.
- Honour **[[Multi-Tenancy]]**: every query, entity, and endpoint is tenant-scoped.
- Ship **[[Arabic RTL]]-ready** UI. RTL is mandatory, not optional.

**Defaults**
- Configuration over hardcoded logic. If a client policy *could* differ, make it data — see [[Master Data Engine]].
- Explicit, reproducible calculations over clever shortcuts.
- When unsure about a business rule, consult [[DOMAIN_MAP]] before coding.

---

## 3. Tech Stack (authoritative)

Canonical home: **[[Tech Stack]]**. Summary:

- **Backend** — C# / .NET 8, ASP.NET Core Web API, EF Core 8 (Npgsql) + Dapper, MediatR (CQRS), FluentValidation, AutoMapper, JWT, Hangfire, Serilog, QuestPDF/ClosedXML.
- **Frontend** — Next.js 16.2.6 (App Router), React 19.2.4, TypeScript 5, Tailwind 4, shadcn/ui, React Hook Form + Zod, React Flow, React Grid Layout + Recharts, dnd-kit, Sonner. ⚠️ **This is NOT the Next.js you know** — Next 16 has breaking changes; read `node_modules/next/dist/docs/` before writing frontend code (see [[AGENTS Directive]]).
- **Data** — PostgreSQL 16 (Azure Flexible Server), Redis (infra-ready, not provisioned), S3 / Cloudflare R2 for files. See [[Database Design]] · [[Deployment and Infrastructure]].

---

## 4. Modules & Engines

**17 backend modules** — full list in [[MODULE_INDEX]]:
[[Core]] · [[Employees]] · [[Attendance]] · [[Payroll Engine|Payroll]] · [[Workflows]] · [[Documents]] · [[Tasks]] · [[Loans]] · [[Expenses]] · [[ESS]] · [[Dashboards]] · [[Reports]] · [[Notifications]] · [[Identity]] · [[Tenancy]] · [[Settings]] · [[Platform]]

**Signature engines** — [[Financial Calculation Engine]], [[Formula Engine]], [[Rule Engine]], [[Immutable Ledger]], [[Workflow Engine]], [[Snapshot and Versioning]], [[Dependency Graph Execution]], [[Scope Engine]], [[Completion Effects Engine]].

---

## 5. Coding Philosophy

1. **Configurable over hardcoded** — if a policy could vary, it's data. → [[Configuration over Hardcoding]]
2. **Auditable & reproducible** — every payroll number recomputable from stored inputs. → [[Reproducibility]]
3. **Immutable finance** — ledger entries are appended or reversed, never edited. → [[Immutable Ledger]]
4. **Clean Architecture discipline** — Domain has zero infrastructure dependencies.
5. **DDD bounded contexts** — cross-module talk goes through contracts/events, never another module's tables. → [[Cross-Module Integration]]
6. **Full-stack or not done** — backend + frontend together.
7. **RTL-first UX** — design references: HubSpot, Linear, Stripe, Notion. → [[Arabic RTL]]

---

## 6. Database Rules

Canonical home: [[Database Design]] · [[Multi-Tenancy]].

- **Migrations only** — schema changes via EF Core migrations; never hand-edit prod. → [[Changelog Index]]
- **Tenant-scoped by default** — every business entity carries `TenantId`; every query filters by it.
- **Audit fields everywhere** — `CreatedAt/By`, `UpdatedAt/By`, soft-delete flags on mutable entities.
- **Never delete financial rows** — reverse, don't edit ([[Immutable Ledger]]).
- **SSL required**; **Dapper for reads**, **EF Core for writes**.

---

## 7. Testing Expectations

- **[[TDD]]** for [[Employees]], [[Workflows]], [[Payroll Engine|Finance/Payroll]] — failing test first.
- Payroll math needs **deterministic** cases (same inputs → same outputs).
- New endpoints: at least one happy-path + one tenant-isolation test.
- Current suite: **~161 backend tests** (Finance-heavy). See [[Test Suite]].

---

## 8. Definition of Done

A change is done only when **all** are true:

- [ ] Backend + frontend both implemented (full-stack).
- [ ] Policy that could vary is configurable — no new hardcoded rules.
- [ ] Tenant isolation verified for every new query/endpoint.
- [ ] Audit fields populated; financial data immutable.
- [ ] Unit tests written and passing (TDD modules especially).
- [ ] EF Core migration created if schema changed.
- [ ] Arabic RTL renders correctly.
- [ ] Swagger reflects new/changed endpoints.
- [ ] No mock data left where a live API exists.
- [ ] Serilog logging added for meaningful operations.

---

## 9. Operational Facts (live infra)

- **API:** `https://hrcloud-api-v4xd.azurewebsites.net` (Swagger at `/swagger`, currently Development mode).
- **DB:** PostgreSQL 16, Azure Flexible Server, UAE North, DB `hrcloud`. Secrets in Azure Key Vault `secretpulse`.
- **Frontend:** Vercel (Next.js). Env var `NEXT_PUBLIC_API_URL`.
- Full detail + gotchas (zip-deploy, CORS, cross-region latency): [[Deployment and Infrastructure]].

> Related: [[AGENTS Directive]] (the Next.js-16 warning), [[Development Standards]] (the 11-section feature design template).
