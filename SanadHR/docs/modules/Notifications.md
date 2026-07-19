---
title: Notifications
aliases: [Notifications Module, HR.Modules.Notifications, Notification Engine]
tags: [module]
---

# Notifications

> In-app bell + email notifications, driven by rules and workflow/state events.
> Up: [[MODULE_INDEX]]

## Purpose
Dispatch notifications (in-app + email) triggered by configurable rules and cross-module events; power the approval bell.

## Architecture
`HR.Modules.Notifications` — application-only module; controllers `Notifications`, `NotificationRules` under [[Platform]].

## Entities
`Notification`, `NotificationRule`, `NotificationDispatch`, `EmailNotificationQueue`.

## Services
Rule evaluation + dispatch; email queue.

## Events
Subscribes to [[Workflows|workflow]] transitions and other module events ([[Cross-Module Integration]]).

## Dependencies
[[Workflows]] (triggers), [[Identity]] (recipients).

## API
`api/notifications`, `api/notifications/rules`. → [[API Endpoint Map]]. Frontend: bell component polls approvals every 60s; `/settings/notifications`.

## Current Status
✅ Built + live (in-app + email).

## Future Work
WhatsApp integration; Email/SMS channel templates → [[ROADMAP]].

## Related Notes
[[Workflows]] · [[Request Center]] · [[Access Management]]
