---
title: Workflows
aliases: [Workflow, Workflows Module, FlowBuilder, HR.Modules.Workflows]
tags: [module, workflow]
---

# Workflows

> The **FlowBuilder** no-code approval engine (`flow_*` tables). Distinct from the graph-based [[Workflow Engine]] under [[Platform]] — the two coexist by isolation ([[DECISION_LOG|WF-1]]).
> Up: [[MODULE_INDEX]]

## Purpose
Let admins build linked-list approval workflows visually (React Flow) and execute them as an atomic, idempotent state machine.

## Architecture
`HR.Modules.Workflows` (namespace `FlowBuilder`) — execution handlers `ApprovalStepHandler`, `ConditionStepHandler`, `EmailActionHandler`; controllers `WorkflowDefinitions`, `WorkflowRequests`, `Workflows`. Runner + graph validator in the domain engine.

## Entities
`WorkflowDefinition`, `WorkflowStep`, `WorkflowRequest`, `WorkflowAuditTrail` (`HR.Domain/Engines/FlowBuilder/`, tables `flow_*`). Transitions use **soft pointers** (`NextStepIdSuccess/Failure` as `Guid?`), not FKs ([[DECISION_LOG|WF-2]]).

## Services
`WorkflowRunner` (state machine; atomic transaction + single SaveChanges; finished request → 409), `WorkflowGraphValidator` (server-side dangling/cycle detection), `IWorkflowStepHandler` strategy per step type ([[DECISION_LOG|WF-3]]).

## Events
State transitions can trigger [[Notifications]] and spawn [[Tasks]].

## Dependencies
[[Notifications]] (email action), [[Identity]] (approver resolution). Reuses existing `Workflows.*` permissions.

## API
`api/workflow-definitions`, `api/workflow-requests`. → [[API Endpoint Map]]. Config UI under `/settings/requests/workflows`.

## Current Status
✅ Built; migration `FlowBuilderEngine` on Azure. 14–19 xUnit tests (`WorkflowExecutionTests`, `WorkflowGraphValidatorTests`, `RequestConditionsTests`). Documented prod bug fixed: audit rows added via `DbSet` to force `Added` state (avoided `DbUpdateConcurrencyException`).

## Future Work
Workflow analytics / SLA tracking → [[ROADMAP]].

## Related Notes
[[Workflow Engine]] · [[Request Center]] · [[Completion Effects Engine]] · [[Termination and Restore]] · [[Tasks]]
