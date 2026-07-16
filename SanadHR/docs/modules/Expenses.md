---
title: Expenses
aliases: [Expenses Module, HR.Modules.Expenses, Reimbursements]
tags: [module, finance]
---

# Expenses

> Expense claims / reimbursements that flow into [[Payroll Engine|payroll]] and the [[Immutable Ledger|ledger]].
> Up: [[MODULE_INDEX]]

## Purpose
Capture reimbursable expenses (scope `mine`/`all`) and route them into payroll/ledger.

## Architecture
`HR.Modules.Expenses` — `ExpensesController`.

## Entities
`Expense`.

## Services
Expense CRUD; reimbursement → payroll/ledger flow (in progress).

## Events
Approved reimbursements append to the ledger ([[Cross-Module Integration]]).

## Dependencies
[[Payroll Engine]], [[Immutable Ledger]], [[Workflows]] (approval).

## API
`api/expenses`. → [[API Endpoint Map]]. Frontend: `/expenses`.

## Current Status
🟡 Core built + live API; payroll/ledger flow in progress → [[IMPLEMENTATION_STATUS]].

## Future Work
Receipt OCR / attachment validation → [[ROADMAP]].

## Related Notes
[[Payroll Engine]] · [[Loans]] · [[Immutable Ledger]]
