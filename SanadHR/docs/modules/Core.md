---
title: Core
aliases: [Core Module, HR.Modules.Core]
tags: [module]
---

# Core

> Foundational org structure (branches, departments) and file storage.
> Up: [[MODULE_INDEX]]

## Purpose
Own the shared org primitives and file up/download used across modules.

## Architecture
`HR.Modules.Core` — `BranchesController`, `DepartmentsController`, `FilesController`.

## Entities
`Branch`, `Department` (with geofence coords, org fields), `StoredFile` (`HR.Domain/Engines/Files/`).

## Services
File storage via `IFileStorageService` → `R2FileStorageService` (S3/Cloudflare R2).

## Events
n/a.

## Dependencies
[[Employees]] (org assignment), [[Documents]] (file storage), [[Org Structure]].

## API
`api/branches`, `api/departments`, `api/files`. → [[API Endpoint Map]]. Frontend: `/settings/organization/branches`, `/departments`.

## Current Status
✅ Built + deployed, live.

## Future Work
Bulk org import → [[ROADMAP]].

## Related Notes
[[Org Structure]] · [[Employees]] · [[Documents]]
