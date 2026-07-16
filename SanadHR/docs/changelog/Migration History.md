---
title: Migration History
aliases: [Migrations, Schema History, EF Migrations]
tags: [changelog, database]
---

# Migration History

> 30 EF Core migrations in `HR.Infrastructure/Migrations/` (Jun–Jul 2026). Migrations are the source of schema truth ([[Database Design]]).
> Up: [[Changelog Index]]

| # | Migration | Theme |
|---|---|---|
| 1 | `InitialCreate` | core HR schema (applied 2026-06-09) |
| 2–5 | EmployeeMasterDataRefs · DepartmentOrgFields · EmployeeOrgPayrollFields · StoredFiles | employees, org, files |
| 6–8 | RequestCenter · ApprovalCenter · CompanyProfileFields | [[Request Center]], approvals |
| 9–12 | DocumentPlatform · RequestImpactsAndSalary · EmployeeDocuments · NotificationRules | [[Document Platform]], impacts, notifications |
| 13–15 | AttendanceEngine · LeaveRecordsEngine · AttendanceHolidays | [[Attendance]] + leave |
| 16–18 | FlowBuilderEngine · RequestApprovalStepRules · CompletionEffects | [[Workflows]], [[Completion Effects Engine]] |
| 19 | SaudiLaborLawAndLeaveAccrual | [[End of Service]] + accrual |
| 20–22 | FinancialEngineFoundation · PayrollSnapshotsAndValidation · PayrollExecutionItems | [[Financial Calculation Engine]] (P1–P4) |
| 23–24 | AccessManagement · BackfillSystemRolePermissions | [[Access Management]] |
| 25–26 | TerminationApproval · EmployeeRestoreRequests | [[Termination and Restore]] |
| 27 | PayrollTypesAndScope | [[Payroll Types Scope Cutoff]] (Sub-project 1) |
| 28 | PayrollTransactions | [[Subproject 2A Transaction Records]] |
| 29 | AttendanceDeductionReference | [[Subproject 2D Attendance Deduction Records]] |
| 30 | AttendancePayrollImpactPermission | [[Subproject 2E Attendance Daily Overtime Excuse]] (seed-data) |

Narrative: core HR → request/approval centers → documents/notifications → attendance & leave → FlowBuilder + completion effects → Saudi labor law → **financial engine foundation → payroll snapshots/validation → execution items → access mgmt → termination/restore → payroll types/scope → transactions → attendance-deduction integration**.

## Related
[[Database Design]] · [[Financial Calculation Engine]] · [[Specs Index]] · [[Release Notes]]
