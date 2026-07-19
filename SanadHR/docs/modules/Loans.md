---
title: Loans
aliases: [Loans Module, HR.Modules.Loans, Advances]
tags: [module, finance]
---

# Loans

> Employee loans & advances with installment schedules that feed [[Payroll Engine|payroll]] deductions.
> Up: [[MODULE_INDEX]]

## Purpose
Track loans/advances and their installments, deducting scheduled amounts during payroll runs.

## Architecture
`HR.Modules.Loans` — `LoansController` over Infrastructure entities.

## Entities
`Loan`, `LoanInstallment` (`kind: Loan | Advance`).

## Services
Loan CRUD + installment scheduling; payroll deduction integration (in progress).

## Events
Installments surface as payroll deductions ([[Cross-Module Integration]]).

## Dependencies
[[Payroll Engine]] (deductions), [[Immutable Ledger]].

## API
`api/loans`. → [[API Endpoint Map]]. Frontend: `/loans` (Loans + Advances).

## Current Status
🟡 Core built + live API; payroll deduction integration in progress → [[IMPLEMENTATION_STATUS]].

## Future Work
Loan approval workflow templates → [[ROADMAP]].

## Related Notes
[[Payroll Engine]] · [[Expenses]] · [[Payroll Additions and Deductions]]
