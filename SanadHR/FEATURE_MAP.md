---
title: FEATURE_MAP
aliases: [Features Index, Feature Map]
tags: [index, features]
---
 
# ✨ FEATURE_MAP — Cross-Cutting Features

> A **feature** is user-facing capability that may span several [[MODULE_INDEX|modules]]. This maps features → where they live → their status.
> Up: [[Home]] · Modules: [[MODULE_INDEX]] · Domain: [[DOMAIN_MAP]]

---

## Payroll & Finance

| Feature | Lives in | Status | Note |
|---|---|---|---|
| Configurable **Payroll Types** (scope + cutoff + calc settings) | [[Payroll Engine]], [[Financial Calculation Engine]] | ✅ shipped | [[Payroll Types Scope Cutoff]] |
| **Additions & Deductions** as visible records | [[Payroll Engine]] | ✅ shipped | [[Payroll Additions and Deductions]] |
| **Attendance → Deduction** records (no hidden deductions) | [[Attendance]] → [[Payroll Engine]] | ✅ shipped | [[Attendance Payroll Impact]] |
| **Overtime → Addition** + configurable rates + excuse cancel | [[Attendance]] → [[Payroll Engine]] | 🔧 in progress (2E) | [[Subproject 2E Attendance Daily Overtime Excuse]] |
| **Payslips** (preview/print/download/store) | [[Payroll Engine]], [[Documents]] | 🗓️ planned | [[Payroll Run Operations Roadmap]] |
| **Payroll exports** (Excel/PDF/CSV/TXT/bank file) | [[Payroll Engine]], [[Reports]] | 🗓️ planned | [[Payroll Run Operations Roadmap]] |
| **Run void / amend / reissue** | [[Payroll Engine]] | 🗓️ planned | [[Payroll Run Operations Roadmap]] |

## People & Org

| Feature | Lives in | Status | Note |
|---|---|---|---|
| **End-of-Service Settlement** (Saudi Articles 84/85) | [[Employees]], [[Settlement Engine]] | ✅ shipped | [[End of Service]] |
| **Termination approval workflow** | [[Employees]], [[Workflows]] | ✅ shipped | [[Termination and Restore]] |
| **Employee restore** (Manager → HR) | [[Employees]] | ✅ shipped | [[Termination and Restore]] |
| **Org chart / reporting lines** | [[Platform]], [[Employees]] | ✅ shipped | [[Org Structure]] |

## Platform capabilities

| Feature | Lives in | Status | Note |
|---|---|---|---|
| **Access Management** (users/roles/permissions/templates, deny-wins) | [[Identity]], [[Platform]] | ✅ shipped | [[Access Management]] |
| **Request Center** (no-code request types + impacts) | [[Platform]], [[ESS]] | ✅ shipped | [[Request Center]] |
| **Approval Workflows** (approver-dropdown wizard) | [[Platform]], [[Workflows]] | ✅ shipped | [[Request Center]] |
| **Document Template Builder** (JSON blocks, tokens, branding) | [[Documents]] | ✅ shipped | [[Document Platform]] |
| **Dashboard Platform** (object-driven widgets) | [[Dashboards]] | ✅ shipped | [[Dashboards]] |
| **Master Data / Object Registry / Metadata** | [[Platform]] | ✅ shipped | [[Master Data Engine]] |
| **Notifications** (in-app bell + email) | [[Notifications]] | ✅ shipped | [[Notifications]] |

---

## Feature ↔ Engine map

Most features reduce to one or more [[Architecture Overview|signature engines]]:

- Anything money → [[Financial Calculation Engine]] + [[Immutable Ledger]]
- Anything configurable → [[Master Data Engine]] / [[Rule Engine]]
- Anything approval-driven → [[Workflow Engine]] + [[Completion Effects Engine]]
- Anything reproducible → [[Snapshot and Versioning]]
- Anything scoped (who's included) → [[Scope Engine]]

Status legend follows [[ROADMAP]]: ✅ Done · 🔧 In progress · 🗓️ Planned.

---

## Before designing any feature — study the competition

Every feature above must beat the market, not match it. Before designing, run the [[COMPETITORS|Competitive Intelligence]] research rule and cite it in the spec:

- Payroll → [[Jisr]], [[Workday]], [[ADP Workforce Now]], [[Gusto]]
- Attendance → [[UKG Pro]], [[Bayzat]], [[ZenHR]]
- Workflows/approvals → [[Rippling]], [[Monday]], [[Jira]]
- Dashboards/reports → [[Stripe Dashboard]], [[Workday]], [[HiBob]]
- ESS/mobile → [[Bayzat]], [[Darwinbox]], [[BambooHR]]
- Config/UX → [[Notion]], [[Linear]], [[HubSpot]]

Full directory: [[Competitor Index]]. Golden rule: if ours is only *equal*, the design isn't finished.
