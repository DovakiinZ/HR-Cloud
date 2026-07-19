---
title: API Endpoint Map
aliases: [Endpoints, Controllers, Route Map]
tags: [api, reference]
---

# API Endpoint Map

> 53 controllers, all in the modules (none in `HR.Api`). Route prefix → module. Exact schemas: Swagger.
> Up: [[API Index]] · Conventions: [[API Guide]]

## Core / People / Payroll

| Prefix | Controller | Module |
|---|---|---|
| `api/auth` | Auth (login/refresh/logout) | [[Identity]] |
| `api/users`, `api/roles`, `api/access` | Users/Roles/Access | [[Identity]] · [[Access Management]] |
| `api/branches`, `api/departments`, `api/files` | Core | [[Core]] |
| `api/employees` (+ `/{id}/documents`) | Employees | [[Employees]] |
| `api/terminations`, `api/restores` | Termination/Restore | [[Employees]] · [[Termination and Restore]] |
| `api/attendance`, `api/shifts` | Attendance + settings | [[Attendance]] |
| `api/attendance/payroll-impact/sync` | Daily payroll impact (2E) | [[Attendance]] |
| `api/payroll` | Runs, transactions, types, ledger | [[Payroll Engine]] |
| `api/payroll/attendance-deductions/sync` | Sync-now (2D) | [[Payroll Engine]] |

## Workflows / Requests / Tasks / Money

| Prefix | Controller | Module |
|---|---|---|
| `api/workflow-definitions`, `api/workflow-requests` | FlowBuilder | [[Workflows]] |
| `api/approvals`, `api/approval-workflows` | Approval center | [[Platform]] · [[Request Center]] |
| `api/requests` | Request center | [[Platform]] · [[Request Center]] |
| `api/leaves` | Leaves | [[Platform]] |
| `api/tasks` | Tasks | [[Tasks]] |
| `api/expenses`, `api/loans` | Expenses/Loans | [[Expenses]] · [[Loans]] |
| `api/settings` | Company settings | [[Settings]] |
| `api/ess` | Self-service | [[ESS]] |
| `api/notifications` (+ `/rules`), `api/lookups` | Notifications/Lookups | [[Notifications]] · [[Platform]] |

## Platform admin (`api/platform/*`)

`admin`, `audit`, `automation-rules`, `company-config`, `master-data`, `metadata-definitions`, `objects` (registry), `registry`, `org-graph`, `reports`, `dashboards` (+ `/widget-data`), `documents`, `forms`, `page-templates`, `permission-templates`, `timeline`, `tokens`, `workflows` (graph engine), `workflow-enhancements`. → [[Platform]]

## Related
[[API Guide]] · [[MODULE_INDEX]] · [[Platform]]
