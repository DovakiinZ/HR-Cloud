---
title: Tenancy
aliases: [Tenancy Module, HR.Modules.Tenancy]
tags: [module]
---

# Tenancy

> Multi-tenant resolution and isolation wiring. The technical model is [[Multi-Tenancy]].
> Up: [[MODULE_INDEX]]

## Purpose
Resolve the current tenant and wire the isolation that keeps every query tenant-scoped.

## Architecture
`HR.Modules.Tenancy` — DI wiring only (isolation is enforced in the shared `ApplicationDbContext` via global query filters).

## Entities
`Tenant` (`HR.Domain/Entities/Tenancy/`); base type `TenantEntity` on all business entities.

## Services
Tenant resolution + `IBackgroundExecutionContext` for ambient tenant scope in Hangfire jobs.

## Events
n/a.

## Dependencies
Underlies **every** module ([[Cross-Module Integration]]).

## API
n/a (no direct controller).

## Current Status
✅ Built; enforced everywhere.

## Future Work
Per-tenant config surface → [[ROADMAP]].

## Related Notes
[[Multi-Tenancy]] · [[Database Design]] · [[Identity]]
