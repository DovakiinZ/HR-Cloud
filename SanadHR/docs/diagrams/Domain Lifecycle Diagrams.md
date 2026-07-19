---
title: Domain Lifecycle Diagrams
aliases: [Lifecycle Diagrams, Business Flows]
tags: [diagram, domain]
---

# Domain Lifecycle Diagrams

> The four business lifecycles at a glance. Narrative + rules in the [[DOMAIN_MAP|domain notes]].
> Up: [[Diagrams Index]] · Domain: [[DOMAIN_MAP]]

## Employee — [[Employee Lifecycle]]
```
Onboarding → Active Employment → Changes → Offboarding → Settlement
```

## Payroll — [[Payroll Lifecycle]]
```
Inputs → Rule Evaluation → Preview/Snapshot → Approval → Ledger Post → Payslip
```
(state detail: [[Payroll Run State Machine]])

## Attendance — [[Attendance Lifecycle]]
```
Capture → Validate → Aggregate → Payroll Impact
```

## Request / ESS — [[Request Lifecycle]]
```
Employee Request → Workflow Instance → Approval Routing → Resolution → Document/Notification
```

## Related
[[DOMAIN_MAP]] · [[Employee Lifecycle]] · [[Payroll Lifecycle]] · [[Attendance Lifecycle]] · [[Request Lifecycle]]
