---
title: Completion Effects Engine
aliases: [Completion Effects, Effect Executors, Request Impacts]
tags: [architecture, engine]
---

# Completion Effects Engine

> A plug-in orchestrator that runs **side effects** when a request/workflow completes — the mechanism behind "request impacts" (create a leave, an attendance correction, an expense, a document…).
> Up: [[Architecture Index]] · Feature: [[Request Center]]

`ICompletionEngine` + `IEffectExecutor` + `EffectExecutorRegistry` (`HR.Application/Engines/Completion/`); domain entities `CompletionRun`, `CompletionEffect`.

- Each effect type has an `IEffectExecutor`, discovered via **`AddEffectExecutorsFromAssembly`** — the same [[Cross-Module Integration|provider/registry pattern]] as the [[Scope Engine]] and [[Workflow Engine]] step handlers.
- An approved request emits `EffectIntent`s; the registry dispatches each to its executor.
- Examples in 2E: `AttendanceCorrectionExecutor` (zero penalty minutes when marking Present), `AttendanceApplyLeaveDaysExecutor` (upsert OnLeave day) — see [[Subproject 2E Attendance Daily Overtime Excuse]].

Adding a new impact = new executor + one DI line; the engine never changes (Open/Closed).

## Related
[[Request Center]] · [[Workflow Engine]] · [[Attendance]] · [[Cross-Module Integration]]
