---
title: Dashboards
aliases: [Dashboards Module, HR.Modules.Dashboards, Dashboard Platform]
tags: [module]
---

# Dashboards

> Object-driven dashboard/widget builder — a live-model catalog + dynamic-SQL aggregation engine.
> Up: [[MODULE_INDEX]]

## Purpose
Build dashboards from auto-discovered objects (44+ models), with a wizard/grid builder, widgets, filters, drilldown, and sharing.

## Architecture
`HR.Modules.Dashboards` — `DashboardsController` (application-only). Uses the [[Master Data Engine|Object Registry]] catalog + a dynamic-SQL aggregation engine.

## Entities
`DashboardDefinition`, `DashboardCategory`, `DashboardTemplate`, `DashboardShare`, `DashboardWidget`, `WidgetDefinition`, `WidgetDataSource`, `WidgetLayout`, `WidgetFilter`, `WidgetDrilldown`, `WidgetPermission`.

## Services
Widget-data aggregation engine; catalog auto-discovery.

## Events
n/a.

## Dependencies
[[Master Data Engine]] (object/field catalog), [[Access Management]] (widget permissions).

## API
`api/platform/dashboards`, `api/platform/dashboards/widget-data`. → [[API Endpoint Map]]. Frontend: `/dashboard`, `/dashboard/builder`, `/dashboard/templates`. Types in `src/types/dashboard.ts`; client CSV/PNG export in `dashboard-export.ts`.

## Current Status
✅ Built + verified (no new migration; object registry reused).

## Future Work
More chart types, scheduled snapshots → [[ROADMAP]].

## Related Notes
[[Reports]] · [[Master Data Engine]] · [[Access Management]]
