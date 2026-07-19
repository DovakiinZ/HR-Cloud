---
title: API Guide
aliases: [REST API Guide, API Conventions]
tags: [api, reference]
---

# API Guide — REST Conventions

> Up: [[API Index]] · Endpoints: [[API Endpoint Map]] · Hosting: [[Deployment and Infrastructure]]

## Base URL

| Environment | Base URL |
|---|---|
| Production API | `https://hrcloud-api-v4xd.azurewebsites.net` |
| Swagger UI | `https://hrcloud-api-v4xd.azurewebsites.net/swagger` |

> Swagger (Swashbuckle) is the **live authoritative** endpoint reference. This guide covers conventions.

## Authentication

- **JWT Bearer.** Obtain a token via `POST /api/auth/login`, then send `Authorization: Bearer <token>`.
- Tokens carry **tenant** and **permission** claims; every request is tenant-scoped and permission-enforced ([[Access Management]], [[Multi-Tenancy]]).
- The frontend reads permission claims client-side via `usePermissions()` (`src/lib/permissions.ts`).

## Request / response

- Content type `application/json` (UTF-8; Arabic supported).
- Standard envelope — unwrapped client-side by `api-client.ts`:
```json
{ "data": {}, "success": true, "message": "string | null", "errors": {} }
```
- Lists support `?page=1&pageSize=25&sort=createdAt`.
- File generation returns downloadable PDF/XLSX or a storage URL (S3/R2).

## Error handling

| Status | Meaning |
|---|---|
| 400 | Validation error (bad input) |
| 401 | Missing/invalid JWT → client clears session, redirects `/login` |
| 403 | Authenticated but lacks permission |
| 404 | Not found (or not in your tenant) |
| 409 | Conflict (duplicate, invalid state transition) |
| 422 | Business rule violation (`DomainException`; added in 2C hotfix so real reasons reach the client) |
| 500 | Server error (logged via Serilog) |

## Rate limits
No app-level rate limiting; throughput constrained by Azure App Service **F1 (Free)** — treat as best-effort until upgraded ([[ROADMAP]]).

## Conventions for new endpoints
Tenant-scope every query · return the standard envelope · document via Swagger · enforce permissions via `RequirePermissionAttribute` · log with Serilog.

## Related
[[API Endpoint Map]] · [[Identity]] · [[Access Management]] · [[Deployment and Infrastructure]]
