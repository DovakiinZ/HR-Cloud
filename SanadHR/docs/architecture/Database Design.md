---
title: Database Design
aliases: [Database, Schema, DB Design]
tags: [architecture, database]
---

# Database Design — Schema & Principles

> PostgreSQL 16 on Azure Flexible Server. EF Core migrations are the source of truth.
> Up: [[Architecture Index]] · Entities: [[DOMAIN_MAP]] · Rules: [[CLAUDE]] · ERD: [[Database Relationships]]

## Engine & location

| Property | Value |
|---|---|
| Engine | PostgreSQL 16 |
| Host | Azure Database for PostgreSQL — Flexible Server |
| Region | UAE North · Tier B1ms (Burstable) |
| Database | `hrcloud` · User `hradmin` · SSL **required** |
| Schema source | EF Core migrations (`InitialCreate` applied 2026-06-09) — see [[Migration History]] |
| Read path | Dapper (reporting/hot queries) |
| Write path | EF Core (domain writes) |

All entities map into a **single `ApplicationDbContext`** (~153 DbSets), configured via one config file per engine area (`FinanceConfigurations`, `WorkflowConfigurations`, `LeaveAttendanceConfigurations`, `PermissionsConfigurations`, …). There is **no per-module schema prefix** — except the [[Workflows]] FlowBuilder tables which are isolated under a `flow_*` prefix.

## Multi-tenancy

App-layer isolation with a tenant key per entity and global query filters. Full model: [[Multi-Tenancy]].

## Audit fields (standard on mutable entities)

`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `TenantId`, `IsDeleted` (where applicable). Base types: `BaseEntity`, `AuditableEntity`, `TenantEntity`.

> **Financial/ledger tables are append-only** — no updates or hard deletes; corrections are reversing entries. See [[Immutable Ledger]].

## Entity catalog (by domain)

Full inventory lives in the module notes; high-level grouping:

- **Identity/Tenancy** — Tenant, User, Role, Permission, RefreshToken → [[Identity]], [[Tenancy]]
- **Employees/Org** — Employee, EmployeeAllowance/Addition/Deduction, Branch, Department, OrgNode/Edge, Position, Grade, CostCenter → [[Employees]], [[Org Structure]]
- **Attendance/Leave** — AttendanceRecord, AttendancePunch, Shift, LeaveRecord, LeaveBalance → [[Attendance]]
- **Finance** — FinancialLedgerEntry, PayrollDefinition(+Version), RuleSet(+Version)+Rule, PayrollRun(+Item/Population), PayrollPayslip, PayrollTransaction(+AttendanceReference) → [[Financial Calculation Engine]]
- **Loans/Expenses** — Loan, LoanInstallment, Expense → [[Loans]], [[Expenses]]
- **Workflows/Tasks** — WorkflowDefinition/Instance, HrTask → [[Workflows]], [[Tasks]]
- **Documents/Requests** — DocumentTemplate, GeneratedDocument, RequestType, RequestInstance → [[Documents]], [[Request Center]]
- **Platform** — MasterDataItem, MetadataDefinition, ObjectDefinition, Notification, DashboardDefinition, ReportDefinition, AuditEntry → [[Platform]], [[Master Data Engine]]

## Rules recap

1. Schema changes via **EF Core migrations only**.
2. Every business entity is **tenant-scoped**.
3. **Audit fields** on all mutable entities.
4. **Never** update/delete financial rows — append or reverse.
5. **SSL required**.
6. **Dapper** for reads, **EF Core** for writes.

## Storage beyond the RDB
Files/documents → S3 / Cloudflare R2 (URLs in DB). Background jobs → Hangfire tables (PostgreSQL). Cache → Redis (not yet provisioned). See [[Deployment and Infrastructure]].

## Related
[[Database Relationships]] · [[Migration History]] · [[Multi-Tenancy]] · [[Immutable Ledger]]
