---
title: Access Management
aliases: [Access, RBAC, Permissions, Roles]
tags: [feature, security]
---

# Access Management

> Users, roles, permissions, and templates with a unified **deny-wins** resolver that bakes effective permissions into the JWT.
> Up: [[FEATURE_MAP]] · Module: [[Identity]]

## What it is
A Settings surface for managing Users / Roles / Permissions / Permission Templates, built on the existing identity + permission tables. Effective permissions are resolved (templates + overrides + scopes, **deny wins**) and embedded as JWT claims.

## Structure
- **Backend** — `PermissionEvaluator` / `PermissionResolver` unify resolution → token. Permissions seeded with deterministic GUIDs (MD5 of `{Module}.{Name}`).
- **Frontend** — `/settings/access/*` (users, roles, templates, employees, audit) + `AccessGuard`, `permission-matrix`, `usePermission()`.

## Scopes
Company / Branch / Department / Direct Reports / Own Data / Custom Groups ([[Development Standards]]).

## Related
[[Identity]] · [[Multi-Tenancy]] · [[API Guide]] · [[Platform]]
