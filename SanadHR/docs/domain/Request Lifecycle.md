---
title: Request Lifecycle
aliases: [ESS Request Flow, Leave Request Flow]
tags: [domain, lifecycle]
---

# Request Lifecycle (ESS)

> Up: [[DOMAIN_MAP]] · Feature: [[Request Center]] · Modules: [[ESS]], [[Workflows]]

```
Employee Request → Workflow Instance → Approval Routing → Resolution → Document/Notification
```

- Requests originate in the **[[ESS]] portal** — default or custom request types (configurable master data, `ObjectType="RequestType"`).
- Each request spawns a **workflow instance** ([[Workflow Engine|state machine]]).
- Approvals route through the org hierarchy as **[[Tasks|tasks]]**.
- On resolution: **[[Completion Effects Engine|impacts]]** fire (create leave / attendance correction / expense / document), **[[Notifications|notifications]]** send, and PDFs generate.

**Rules**
- Request types are **configurable** (no-code) — [[Configuration over Hardcoding]].
- Approval chains derive from org structure + workflow definition; the no-code **Approval Workflow Wizard** configures approver dropdowns/conditions ([[Request Center]]).

## Related
[[Request Center]] · [[ESS]] · [[Workflows]] · [[Completion Effects Engine]] · [[Notifications]]
