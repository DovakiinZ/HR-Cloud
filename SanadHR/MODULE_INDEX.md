---
title: MODULE_INDEX
aliases: [Modules Index, Module Index, Modules]
tags: [index, modules]
---

# 🧩 MODULE_INDEX — Backend Modules

> The 17 vertical feature slices under `backend/src/HR.Modules/`. Each references [[Clean Architecture Layers|HR.Application]] (heavy logic lives in `HR.Infrastructure/Engines/*`). Every module note follows the same 10-section template: **Purpose · Architecture · Entities · Services · Events · Dependencies · API · Current Status · Future Work · Related Notes**.
>
> Up: [[Home]] · Sibling indexes: [[DOMAIN_MAP]] · [[FEATURE_MAP]] · [[Architecture Index]]

---

## Fully-built modules

| Module | One line | Deep note |
|---|---|---|
| **Identity** | Auth (JWT + refresh), users, roles, permissions, access templates | [[Identity]] |
| **Core** | Branches, departments, file storage | [[Core]] |
| **Employees** | Employee lifecycle, termination trigger, EOS settlement preview, scoping | [[Employees]] |
| **Attendance** | Punches → daily records, shifts, penalties, payroll impact | [[Attendance]] |
| **Payroll** | Runs, additions/deductions, types — the app on the [[Financial Calculation Engine]] | [[Payroll Engine]] |
| **Workflows** | FlowBuilder no-code approval workflows (state machine) | [[Workflows]] |
| **Platform** | Umbrella: approvals, requests, automation, master-data, metadata, org-graph, reports, dashboards, terminations, timeline, tokens, forms, object registry | [[Platform]] |
| **Tasks** | HR task management (CQRS); frontend still on mock data | [[Tasks]] |
| **Settings** | Company settings | [[Settings]] |
| **Tenancy** | Multi-tenant resolution / isolation wiring | [[Tenancy]] |

## Feature-surface modules (thin controllers over Platform/Infra engines)

| Module | One line | Deep note |
|---|---|---|
| **Loans** | Loans & advances + installments, feed payroll deductions | [[Loans]] |
| **Expenses** | Expense claims, feed payroll/ledger | [[Expenses]] |
| **Documents** | Template-driven PDF generation (QuestPDF, RTL) | [[Documents]] |
| **Reports** | Report builder + exports | [[Reports]] |
| **Dashboards** | Object-driven dashboard/widget builder | [[Dashboards]] |
| **Notifications** | In-app + email notification engine | [[Notifications]] |
| **ESS** | Employee self-service portal surface | [[ESS]] |

---

## How to read a module note

Every note above answers the same 10 questions so you can jump between them without re-orienting:

1. **Purpose** — why the module exists
2. **Architecture** — where it lives, how it's layered
3. **Entities** — the domain objects it owns
4. **Services** — the engines/services that do the work
5. **Events** — domain events it publishes/handles
6. **Dependencies** — what it relies on ([[Cross-Module Integration]])
7. **API** — its controllers/route prefixes → [[API Endpoint Map]]
8. **Current Status** — built / partial / skeleton
9. **Future Work** — roadmap items → [[ROADMAP]]
10. **Related Notes** — backlinks into the graph

> Note: a *module* is a deployment slice; a *[[FEATURE_MAP|feature]]* may cross several modules. Two engines named "Workflow" coexist — the **FlowBuilder** engine (the [[Workflows]] module) and the graph-based **[[Workflow Engine]]** under [[Platform]].
