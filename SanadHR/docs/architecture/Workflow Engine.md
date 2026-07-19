---
title: Workflow Engine
aliases: [Workflow, Workflows Engine, Approval Engine]
tags: [architecture, engine, workflow]
---

# Workflow Engine

> State-machine automation with a visual, no-code builder. Two engines coexist — this note covers the **graph-based** one (under [[Platform]]); the linked-list **FlowBuilder** engine is the [[Workflows]] module.
> Up: [[Architecture Index]] · Module: [[Workflows]] · Feature: [[Request Center]]

## Two engines, isolated by design ([[Workflows|WF-1]])

| Engine | Where | Model | Tables |
|---|---|---|---|
| **Graph workflow engine** | `HR.Domain/Engines/Workflows/`, [[Platform]] | nodes + edges + conditions + approver rules | workflow_* |
| **FlowBuilder** | `HR.Domain/Engines/FlowBuilder/`, [[Workflows]] module | linked-list steps with soft pointers | `flow_*` |

They coexist with zero collisions (distinct namespace + table prefix + DbSet names).

## Key patterns

- **Step handlers = Strategy + Open/Closed** — `IWorkflowStepHandler` per type, resolved by DI collection. New step type = new handler + one DI line; the engine never changes.
- **Soft pointers, not FKs** for transitions (`NextStepIdSuccess/Failure` as `Guid?`) — free rewiring, no EF cascade-cycle constraints.
- **`Config`/`Payload` as `jsonb`** — schema-less, still queryable.
- **`WorkflowRunner` = explicit state machine** — applies a decision, auto-advances non-blocking steps, parks on the next Approval or terminal; **atomic** (transaction + single SaveChanges) and **idempotent** (a finished request returns 409, never double-applies).
- **Server-side `WorkflowGraphValidator`** mirrors the client validator (dangling pointers, self-references, cycle detection via DFS) — an invalid/cyclic graph can never be persisted.

Approver types: SpecificUser / DirectManager / DepartmentHead / BranchManager / HrManager / Role / ManagerChain. Conditions: eq/neq/gt/gte/lt/lte/contains.

## Consumers
[[Request Center]] (ESS requests), [[Termination and Restore]] (approval chain), [[Notifications]] (state transitions), [[Tasks]] (approval tasks).

## Related
[[Workflows]] · [[Completion Effects Engine]] · [[Request Center]] · [[Cross-Module Integration]]
