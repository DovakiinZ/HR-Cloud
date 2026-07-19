---
title: Platform
aliases: [Platform Module, HR.Modules.Platform]
tags: [module, platform]
---

# Platform

> The umbrella module — 30 controllers hosting the cross-cutting engines that everything else consumes.
> Up: [[MODULE_INDEX]]

## Purpose
House platform-wide capabilities: approvals, request center, automation, master-data/metadata/object-registry, org-graph, reports, dashboards, terminations, timeline, tokens, forms, the graph [[Workflow Engine]], notifications rules, audit, admin, lookups, permissions, page-templates.

## Architecture
`HR.Modules.Platform` — 30 controllers over the Infrastructure engines. This is where the "build platform engines first" rule ([[Development Standards]]) is realized.

## Entities
[[Master Data Engine|MasterDataObjectType/Item]], `MetadataDefinition/Field/Option/Value`, `ObjectDefinition/Field/Relationship/Permission`, `RequestType/Instance/Approval` ([[Request Center]]), graph `WorkflowDefinition/Instance/Node/Edge` ([[Workflow Engine]]), `AutomationRule/Trigger/Condition/Action`, `TimelineEvent`, `TokenCategory/Definition`, `FormDefinition/Field/Submission`, `AuditEntry`, org-graph `OrgNode/Edge/Layout`.

## Services
`AutomationEngine`, `AuditEngine`, `TimelineEngine`, `TokenResolver`, `WorkflowEngine` (graph), `PermissionEvaluator`/`Resolver`, plus the [[Completion Effects Engine]] and [[Master Data Engine]].

## Events
Automation triggers/conditions/actions; timeline events; audit entries on sensitive actions.

## Dependencies
Consumed by nearly every module ([[Cross-Module Integration]]); depends on [[Identity]] for permissions.

## API
`api/platform/*` (admin, audit, automation-rules, company-config, master-data, metadata-definitions, objects, registry, org-graph, reports, dashboards, documents, forms, page-templates, permission-templates, timeline, tokens, workflows, workflow-enhancements) + feature prefixes `api/approvals`, `api/approval-workflows`, `api/requests`, `api/leaves`, `api/lookups`, `api/notifications`. → [[API Endpoint Map]].

## Current Status
✅ Broad surface built; 🟡 some areas partially wired to the frontend. → [[IMPLEMENTATION_STATUS]].

## Future Work
Consolidation + production hardening → [[ROADMAP]].

## Related Notes
[[Master Data Engine]] · [[Workflow Engine]] · [[Completion Effects Engine]] · [[Request Center]] · [[Access Management]] · [[Org Structure]] · [[Dashboards]] · [[Reports]]
