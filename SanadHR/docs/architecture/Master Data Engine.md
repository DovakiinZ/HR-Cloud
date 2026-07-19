---
title: Master Data Engine
aliases: [Master Data, Object Registry, Metadata Engine, Configurable Catalogs]
tags: [architecture, engine, platform]
---

# Master Data Engine

> The engine behind "configurable over hardcoded" — one generic table drives every customer-configurable catalog.
> Up: [[Architecture Index]] · Rule: [[Configuration over Hardcoding]] · Governance: [[ADR-No-Duplicate-Fields]]

Three related layers under [[Platform]]:

- **Master Data** — `MasterDataObjectType` + `MasterDataItem`: one generic table keyed by `ObjectType` (~30 types) holding lookup/reference data. Examples: Addition/Deduction types, `PayrollTypeCategory`, `PayrollExportFormat`, Request Types, Nationalities, Contract Types, Leave Types. Rich behavior lives in `MetadataJson` (e.g. a `PayrollExportFormat` maps to a code exporter via `MetadataJson.handlerKey`).
- **Metadata** — `MetadataDefinition` / `MetadataField` / `MetadataOption` / `MetadataValue`: dynamic custom fields on entities.
- **Object Registry** — `ObjectDefinition` / `ObjectField` / `ObjectRelationship` / `ObjectPermission`: a dynamic object/entity catalog (also powers the [[Dashboards]] object catalog).

## Why it matters

New configurable catalog = **a new `MasterDataObjectType`, not a new table** ([[ADR-No-Duplicate-Fields|schema governance]]). Engine logic keys on **stable codes** (e.g. `ABSENCE`/`LATE`/`SHORTAGE`), while presentation (labels, order, enable/disable) is customer-editable master data — see [[ADR-Attendance-Penalty-Kind]].

Boundary: a genuinely new file *format* still needs a code handler even though the catalog entry is master data ([[Payroll Types Scope Cutoff|D4]]).

## Related
[[Platform]] · [[Configuration over Hardcoding]] · [[Rule Engine]] · [[Payroll Types Scope Cutoff]] · [[Dashboards]]
