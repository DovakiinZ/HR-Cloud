---
title: Tasks
aliases: [Tasks Module, HR.Modules.Tasks, Task Management]
tags: [module]
---

# Tasks

> HR task management (board/list/calendar). Backend is full CQRS; the **frontend still runs on mock data** — the main remaining mock→live gap.
> Up: [[MODULE_INDEX]] · Status: [[IMPLEMENTATION_STATUS]]

## Purpose
Create, assign, and track HR tasks with checklists, comments, and activity — sourced from lifecycle events (terminations, payroll, attendance, onboarding, recruitment, automation).

## Architecture
`HR.Modules.Tasks` — CQRS handlers (Create/Update/Delete/Comment, Get); `TasksController`.

## Entities
`HrTask`, `HrTaskChecklist`, `HrTaskComment`, `HrTaskActivity` (`HR.Domain/Entities/Tasks/`). Tags stored as PostgreSQL `jsonb`.

## Services
CQRS task handlers.

## Events
Can be spawned by [[Workflows|workflow]] states and other module events ([[Cross-Module Integration]]).

## Dependencies
[[Workflows]] (approval tasks), [[Identity]] (assignment).

## API
`api/tasks`. → [[API Endpoint Map]]. Frontend: `/tasks`, `/tasks/board`, `/calendar`, `/my-tasks`, `/team`, `/settings`, `/templates`.

## Current Status
✅ Backend + tests. 🔴 Frontend on `src/lib/tasks-mock-data.ts` — **not wired to the live API**.

## Future Work
Wire the Tasks UI to the live API (highest mock→live priority) → [[PROJECT_STATUS]].

## Related Notes
[[Workflows]] · [[IMPLEMENTATION_STATUS]] · [[Cross-Module Integration]]
