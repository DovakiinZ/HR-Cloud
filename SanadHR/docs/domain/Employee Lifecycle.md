---
title: Employee Lifecycle
aliases: [Employee Journey, Onboarding to Settlement]
tags: [domain, lifecycle]
---

# Employee Lifecycle

> Up: [[DOMAIN_MAP]] · Module: [[Employees]] · Diagram: [[Domain Lifecycle Diagrams]]

```
Onboarding → Active Employment → Changes → Offboarding → Settlement
```

| Stage | What happens |
|---|---|
| **Onboarding** | Create employee, assign to company/org unit, set contract, salary structure, role. |
| **Active** | Attendance recorded, leave taken, payroll run monthly, documents issued. |
| **Changes** | Promotions, transfers, salary revisions — each **versioned** for history. |
| **Offboarding** | Termination or resignation triggers an approval workflow ([[Termination and Restore]]). |
| **Settlement** | Final pay, [[End of Service|end-of-service benefits]], ledger closure. |

**Rules**
- Every employee belongs to exactly one **tenant** and one **company** (multi-company within a tenant — [[Multi-Tenancy]]).
- Salary and policy changes are **versioned**, never overwritten ([[Snapshot and Versioning]]).
- Org hierarchy drives approval routing ([[Org Structure]]).
- A terminated employee can be **restored** (Manager → HR) — see [[Termination and Restore]].

## Related
[[Employees]] · [[End of Service]] · [[Termination and Restore]] · [[Payroll Lifecycle]] · [[Org Structure]]
