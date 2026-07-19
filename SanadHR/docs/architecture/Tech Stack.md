---
title: Tech Stack
aliases: [Technology, Stack, Dependencies]
tags: [architecture, reference]
---

# Tech Stack (authoritative)

> The canonical technology list with versions. Everything else links here — never restate versions elsewhere.
> Up: [[Architecture Index]] · Rules: [[CLAUDE]]

## Backend — C# / .NET 8

| Concern | Technology |
|---|---|
| Runtime | .NET 8.0, ASP.NET Core Web API |
| ORM | Entity Framework Core 8.0.10 (Npgsql / PostgreSQL) |
| Micro-ORM | Dapper (hot paths, reporting) — reads |
| Mediation | MediatR (CQRS commands/queries + notifications) |
| Validation | FluentValidation (MediatR `ValidationBehavior`) |
| Mapping | AutoMapper (incl. Arabic enum labels) |
| Auth | JWT Bearer + refresh tokens; BCrypt password hashing |
| Background jobs | Hangfire 1.8.17 (PostgreSQL storage) |
| Logging | Serilog 8.0.3 (structured) |
| Caching | StackExchange.Redis (infra-ready, **not yet provisioned**) |
| Storage | AWS S3 SDK 3.7.405.2 / Cloudflare R2 |
| Documents | QuestPDF (PDF), ClosedXML (Excel) |
| API docs | Swagger / Swashbuckle |

Solution: **20+ projects**. See [[Clean Architecture Layers]].

## Frontend — TypeScript / Next.js

| Concern | Technology |
|---|---|
| Framework | Next.js 16.2.6 (App Router), React 19.2.4, TypeScript 5 |
| Styling | Tailwind CSS 4 (PostCSS) |
| Components | shadcn/ui, @base-ui/react, lucide-react |
| Forms | React Hook Form 7.76 + Zod 4.4.3 |
| Workflows UI | React Flow (`@xyflow/react` 12.11) |
| Dashboards | React Grid Layout 1.5 + Recharts 3.8 |
| Drag & drop | dnd-kit |
| Toasts | Sonner · Themes: next-themes |
| Dev | `next dev --port 3001` |

> ⚠️ **Next.js 16 has breaking changes vs training data.** Read `node_modules/next/dist/docs/` before writing frontend code. See [[AGENTS Directive]].

Design system (fonts *Thmanyah Sans* + *IBM Plex Mono*, 6 themes, tokens): see [[Design System]]. RTL requirements: [[Arabic RTL]].

## Database — PostgreSQL 16

- **PostgreSQL 16** on Azure Database for PostgreSQL — Flexible Server (UAE North). DB `hrcloud`, user `hradmin`, SSL required.
- EF Core migrations are the source of schema truth. See [[Database Design]] · [[Migration History]].

## Infrastructure

Azure App Service (API) + Vercel (frontend) + Azure PostgreSQL + S3/R2 + Azure Key Vault. Full topology & gotchas: [[Deployment and Infrastructure]].

## Related
[[Architecture Overview]] · [[CLAUDE]] · [[Development Standards]]
