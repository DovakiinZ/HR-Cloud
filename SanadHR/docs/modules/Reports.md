---
title: Reports
aliases: [Reports Module, HR.Modules.Reports, Report Builder]
tags: [module]
---

# Reports

> Object-driven report builder with filters/grouping and PDF/XLSX export.
> Up: [[MODULE_INDEX]]

## Purpose
Let users define reports (fields, filters, grouping, sorting, relationships) over the object catalog and export/schedule them.

## Architecture
`HR.Modules.Reports` — `ReportsController` (application-only). Shares the object catalog with [[Dashboards]] and the [[Master Data Engine|Object Registry]].

## Entities
`ReportDefinition`, `ReportTemplate`, `ReportField`, `ReportFilter`, `ReportGrouping`, `ReportSorting`, `ReportRelationship`, `ReportSchedule`, `ReportShare`.

## Services
Report execution + export (ClosedXML / QuestPDF).

## Events
n/a.

## Dependencies
[[Master Data Engine]] (object catalog), [[Documents]] (PDF export), [[Access Management]].

## API
`api/platform/reports`. → [[API Endpoint Map]]. Frontend: `/reports`.

## Current Status
✅ Built + live (PDF/XLSX export).

## Future Work
Scheduled report delivery → [[ROADMAP]].

## Related Notes
[[Dashboards]] · [[Master Data Engine]] · [[Documents]]
