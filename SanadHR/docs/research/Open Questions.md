---
title: Open Questions
aliases: [Known Issues, Inconsistencies, Tech Debt]
tags: [research, risks]
---

# Open Questions & Known Inconsistencies

> Things that are unsettled, transitional, or worth revisiting. Distinct from the [[ROADMAP]] (planned features) — these are *frictions*.
> Up: [[Research Index]] · Risks: [[PROJECT_STATUS]]

## Frontend / auth
- **Auth guard keys off a legacy `hr_auth` localStorage flag** (`(dashboard)/layout.tsx`) while real auth uses JWT tokens in `auth-storage.ts` — a known transitional state.
- **[[Tasks]] module is mock-only** (`tasks-mock-data.ts`) — highest mock→live gap. See [[IMPLEMENTATION_STATUS]].

## Backend / infra
- **Dev mode in production** — `ASPNETCORE_ENVIRONMENT=Development`, Swagger publicly exposed. Switch before GA ([[Deployment and Infrastructure]]).
- **Redis not provisioned** — caching headroom unused.
- **Cross-region latency** — API (West Europe) ↔ DB (UAE North) ~40ms; co-locate.
- **Free-tier App Service (F1)** — cold starts / throttling risk under load.

## Payroll carry-forwards (from Sub-project 1 verification)
- Pre-existing `AssetType` double-seed.
- `DailyWageFor` uses `Math.Round` ToEven vs the `AwayFromZero` money constraint — adjudicate.
- `WorkingCalendarId` not editable via `UpdateDraftVersionAsync`.
- Run-state guard bug (#4) — partly addressed by [[Payroll Run Operations Roadmap|Area 3]]; still open at run level.

## Documentation
- Two product identities coexist ([[Product Vision]]) — keep both framings but don't let facts drift.
- The real code lives in the parent repo (`backend/`, `src/`, `docs/superpowers/`); this vault documents it. Verify a symbol still exists before recommending it.

## Related
[[PROJECT_STATUS]] · [[IMPLEMENTATION_STATUS]] · [[Deployment and Infrastructure]] · [[ROADMAP]]
