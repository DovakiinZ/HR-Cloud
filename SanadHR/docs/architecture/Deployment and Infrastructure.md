---
title: Deployment and Infrastructure
aliases: [Deployment, Infrastructure, Hosting, DevOps]
tags: [architecture, deployment]
---

# Deployment & Infrastructure

> How SanadHR is hosted, configured, and deployed. Canonical home for all infra facts.
> Up: [[Architecture Index]] · State: [[PROJECT_STATUS]]

## Topology

| Component | Platform | Details |
|---|---|---|
| Frontend | **Vercel** | Next.js 16; env `NEXT_PUBLIC_API_URL` |
| Backend API | **Azure App Service** | `hrcloud-api-v4xd.azurewebsites.net` · F1 Free · West Europe · DOTNETCORE:8.0 |
| Database | **Azure PostgreSQL** | Flexible Server · B1ms Burstable · UAE North · DB `hrcloud` |
| File storage | **AWS S3 / Cloudflare R2** | documents, generated PDFs |
| Cache | **Redis** | infra-ready, **not yet provisioned** |
| Background jobs | **Hangfire** | PostgreSQL-backed |
| Secrets | **Azure Key Vault** `secretpulse` | connection strings, credentials |

> API (West Europe) ↔ DB (UAE North) are **cross-region — ~40ms** latency. UAE North had 0 App Service quota on the free trial, hence the split. Co-location is on the [[ROADMAP]].

## Connection & config

- **Local dev:** `appsettings.json` uses `localhost:5432` (never commit prod secrets here).
- **Production:** inject `ConnectionStrings__DefaultConnection` via env var. Redis connection blanked (not provisioned). `ASPNETCORE_ENVIRONMENT=Development` currently (so Swagger is on — **switch to Production before GA**).
- **Secrets:** DB password in Key Vault `secretpulse` as `hrcloud-db-password`.
- **CORS:** allows Vercel domains (`Cors__Origins__*`).

## Deploying

- **Frontend:** push to the connected branch → Vercel auto-builds. Set `NEXT_PUBLIC_API_URL` → Azure API.
- **Backend:** build `HR.Api`, publish via `az webapp deploy --type zip`. Confirm `/swagger` responds and CORS allows Vercel.
- **Migrations:** apply EF Core migrations against Azure PostgreSQL during release. See [[Migration History]].

### ⚠️ Zip-deploy gotcha
PowerShell `Compress-Archive` writes nested paths with `\`, which Linux Kudu rsync rejects. **Build the zip via `System.IO.Compression.ZipFile` with `.Replace('\\','/')`** on entry names.

### Local CORS workaround
`next.config.ts` rewrites `/api/:path*` → Azure server-side (dev only). `.env.local` sets `NEXT_PUBLIC_API_URL=http://localhost:3001` so the browser hits the same origin and avoids CORS preflight. `api-client.ts` also sanitizes the base URL (strips BOM/zero-width/whitespace).

## Production hardening (before GA)

- Switch to Production profile (disable public Swagger).
- Upgrade App Service tier from F1 (cold starts, throttling).
- Provision Redis; co-locate API + DB in UAE North.
- Load-test payroll batch runs; confirm tenant isolation under concurrent load.
- Verify Azure automated backups + a restore drill.

## Post-deploy checklist
- [ ] `/swagger` reachable / health endpoint responds
- [ ] Login returns a valid JWT
- [ ] A tenant-scoped read returns only that tenant's data
- [ ] Migrations applied (schema matches latest)
- [ ] Document generation writes to S3/R2
- [ ] Hangfire jobs processing
- [ ] Frontend on live API (no mock data on deployed screens)

## Related
[[Tech Stack]] · [[Database Design]] · [[PROJECT_STATUS]] · [[API Guide]]
