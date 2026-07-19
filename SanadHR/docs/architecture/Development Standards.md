---
title: Development Standards
aliases: [Dev Rules, Feature Design Template, Development Rules]
tags: [rules, process]
---

# Development Standards

> Mandatory dev standards from *Development Rules - Saudi HR SaaS Platform.md*. Complements the [[CLAUDE|operating manual]].
> Up: [[Architecture Index]]

## Core rules
- **Every feature = Frontend + Backend** (never one only). Build **platform engines first**, business modules on top.
- **UX must emulate** HubSpot / Linear / Stripe / Notion / ClickUp; avoid old-HR/table-only/static UI. FE: Next.js, TS, Tailwind, shadcn/ui, Framer Motion, [[Arabic RTL|RTL]]. BE: ASP.NET Core .NET 8, PostgreSQL, EF Core, Dapper, Redis, R2.
- **Every table must have:** `TenantId, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, DeletedAt, DeletedBy, IsDeleted` ([[Database Design]]).
- **Permission scopes:** Company / Branch / Department / Direct Reports / Own Data / Custom Groups ([[Access Management]]).
- **Notification channels:** In-App / Email / Push / WhatsApp (future). **Exports:** PDF / XLSX.
- **Platform engines to consume:** Metadata, Object Registry, Permission, Workflow, Automation, Audit, Timeline, Document Token, Dashboard, Notification.

## Required 11-section design structure (per feature)
1. Business Overview → 2. Frontend Architecture → 3. Backend Architecture → 4. Database Design → 5. API Design → 6. Workflow Integration → 7. Permissions → 8. Notifications → 9. Reports/Dashboards → 10. Audit Logs → 11. Development Tasks.

## Related
[[CLAUDE]] · [[Master Data Engine]] · [[Development Process]] · [[Design System]]
