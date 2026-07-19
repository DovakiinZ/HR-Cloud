---
title: Org Structure
aliases: [Organization, Org Chart, Reporting Lines, Org Designer]
tags: [feature, org]
---

# Org Structure

> Branches, departments, positions, grades, cost centers, and a React Flow reporting-line org chart.
> Up: [[FEATURE_MAP]] · Modules: [[Core]], [[Platform]], [[Employees]]

## What it is
The organizational backbone that drives approval routing and scoping. Departments/branches carry `nameAr`/`nameEn` and geofence coords; an org graph (`OrgNode`/`OrgEdge`/`OrgGraphLayout`/`EmployeeReportingLine`) powers a visual department org chart.

## Company config
`Position`, `Grade`, `CostCenter`, `CompanyProfile`, `CalendarSetting`, `FiscalPeriod` (`HR.Domain/Engines/CompanyConfig/`). Note: standalone "Position" was later folded into job titles.

## API / UI
`api/platform/org-graph`, `api/branches`, `api/departments`. Frontend: `/settings/organization/*` (branches, departments, departments/chart, cost-centers, grades, job-titles, nationalities).

## Related
[[Core]] · [[Employees]] · [[Employee Lifecycle]] · [[Scope Engine]] · [[Master Data Engine]]
