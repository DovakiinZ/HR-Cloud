---
title: Termination and Restore
aliases: [Termination Workflow, Employee Restore, Offboarding]
tags: [feature, employees, workflow]
---

# Termination & Restore

> Offboarding via a Manager → HR → Finance approval workflow, and restoring a terminated employee.
> Up: [[FEATURE_MAP]] · Module: [[Employees]] · Domain: [[End of Service]]

## Termination
A termination request routes through **Manager → HR → Finance** approval on the settlement. On final approval the system terminates the employee, creates a **Pending settlement Expense**, and generates the settlement PDF. Backend: `ITerminationWorkflow` + `api/terminations`; UI `/employees/terminations`.

## Restore
A terminated employee can be restored via a **Manager → HR** flow (`EmployeeRestoreRequest`, `IRestoreWorkflow`, `api/restores`). A terminated-search toggle surfaces them.

## Related
[[Employees]] · [[End of Service]] · [[Settlement Engine]] · [[Workflows]] · [[Expenses]] · [[Employee Lifecycle]]
