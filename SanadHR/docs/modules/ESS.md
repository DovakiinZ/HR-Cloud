---
title: ESS
aliases: [ESS Module, HR.Modules.ESS, Employee Self-Service, Self Service]
tags: [module]
---

# ESS — Employee Self-Service

> The employee-facing portal surface where staff submit requests that drive the [[Request Center]] engine.
> Up: [[MODULE_INDEX]] · Feature: [[Request Center]] · Lifecycle: [[Request Lifecycle]]

## Purpose
Give employees a portal to view their data and submit requests (leave, letters, changes) via configurable request types.

## Architecture
`HR.Modules.ESS` — `ESSController` (application-only). Requests are real entities handled by the [[Request Center]] under [[Platform]].

## Entities
Uses `RequestType` / `RequestInstance` (configurable master data, `ObjectType="RequestType"`) — see [[Request Center]].

## Services
Request submission; routes into the [[Workflow Engine]] and [[Completion Effects Engine]].

## Events
Each submitted request spawns a workflow instance ([[Request Lifecycle]]).

## Dependencies
[[Request Center]], [[Workflows]], [[Notifications]], [[Documents]] (generated request PDFs).

## API
`api/ess`, `api/requests`. → [[API Endpoint Map]]. Frontend: `/requests`, request wizards.

## Current Status
✅ Built + live; request submit loop verified.

## Future Work
Mobile ESS app; WhatsApp / Apple Messages channels → [[ROADMAP]].

## Related Notes
[[Request Center]] · [[Request Lifecycle]] · [[Workflows]] · [[Completion Effects Engine]]
