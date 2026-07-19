---
title: Request Center
aliases: [Requests, Request Types, Approval Center, Approval Workflows]
tags: [feature, platform, workflow]
---

# Request Center

> No-code request types that become real entities, route through approvals, and fire **impacts** on resolution.
> Up: [[FEATURE_MAP]] · Modules: [[Platform]], [[ESS]] · Lifecycle: [[Request Lifecycle]]

## What it is
The engine behind ESS requests. Request Types are configurable master data (`ObjectType="RequestType"`) whose `MetadataJson` carries category, linked dynamic form, workflow, SLA, and generated document. A submitted request is a real `RequestInstance` (FK form/workflow/template) — this fixed the old "no form linked" problem.

## Impacts
On approval, the [[Completion Effects Engine]] runs side effects — create a leave, an attendance correction, an expense, a document, a timeline entry, an audit record. New impact = new `IEffectExecutor` + one DI line.

## Approvals
The no-code **Approval Workflow Wizard** (approver-dropdown + condition builder) configures chains on the existing [[Workflow Engine]] (`WorkflowChainConfig`), replacing the older linked-list builder UI. Approver types: SpecificUser / DirectManager / DepartmentHead / BranchManager / HrManager / Role / ManagerChain. Approval Center + bell/email [[Notifications]].

## Related
[[Request Lifecycle]] · [[Workflow Engine]] · [[Completion Effects Engine]] · [[ESS]] · [[Documents]] · [[Master Data Engine]]
