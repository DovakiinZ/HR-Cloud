---
title: Identity
aliases: [Identity Module, Auth, RBAC, HR.Modules.Identity]
tags: [module, security]
---

# Identity

> Authentication, users, roles, and the deny-wins permission model behind [[Access Management]].
> Up: [[MODULE_INDEX]]

## Purpose
Issue and validate JWTs, manage users/roles/permissions, and resolve effective permissions (with templates, overrides, and scopes) into the token.

## Architecture
`HR.Modules.Identity` — `AuthService`, `JwtTokenService`, `PasswordHasher` (BCrypt), `PermissionService`; controllers `Auth`, `Users`, `Roles`, `Access`.

## Entities
`User`, `Role`, `Permission`, `RolePermission`, `UserRole`, `UserPermission`, `RefreshToken`; permission engine `PermissionTemplate(+Item)`, `UserPermissionTemplate/Override/Scope`, `PermissionMerge`.

## Services
`PermissionEvaluator` / `PermissionResolver` (unified **deny-wins** resolver → JWT claims), `AccessTemplateSeeder`, JWT + refresh, BCrypt hashing. Permissions seeded with deterministic GUIDs (MD5 of `{Module}.{Name}`).

## Events
n/a (auth is request-scoped).

## Dependencies
Consumed by every module for authorization ([[API Guide]]); frontend reads claims via `usePermissions()`.

## API
`api/auth` (login/refresh/logout), `api/users`, `api/roles`, `api/access`. → [[API Endpoint Map]]. Frontend: `/settings/access/*` (users, roles, templates, employees, audit) + `AccessGuard` / `permission-matrix`.

## Current Status
✅ Built + deployed; live. `PermissionMergeTests` green ([[Test Suite]]).

## Future Work
Fine-grained scope expansion → [[ROADMAP]].

## Related Notes
[[Access Management]] · [[Multi-Tenancy]] · [[API Guide]] · [[Platform]]
