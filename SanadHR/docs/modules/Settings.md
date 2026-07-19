---
title: Settings
aliases: [Settings Module, HR.Modules.Settings, Company Settings]
tags: [module]
---

# Settings

> Company profile + tenant/system configuration (the canonical `CompanyProfile` used by documents).
> Up: [[MODULE_INDEX]]

## Purpose
Store per-tenant company settings (name, branding, calendar, Saudi defaults) consumed by documents, payroll, and org.

## Architecture
`HR.Modules.Settings` — `SettingsController` + DI.

## Entities
`CompanySettings` / `CompanyProfile` (`HR.Domain/Entities/Settings/`); Saudi defaults seed SAR, Asia/Riyadh, Sunday week-start, 21 annual leave days.

## Services
Settings read/write.

## Events
n/a.

## Dependencies
[[Documents]] (branding/company info on PDFs), [[Payroll Engine]] (fiscal calendar), [[Master Data Engine]].

## API
`api/settings`. → [[API Endpoint Map]]. Frontend: `/settings/company`, `/settings/company-organization`.

## Current Status
✅ Built + deployed, live.

## Future Work
Expand configurable defaults → [[ROADMAP]].

## Related Notes
[[Documents]] · [[Master Data Engine]] · [[Arabic RTL]]
