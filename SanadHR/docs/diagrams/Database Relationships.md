---
title: Database Relationships
aliases: [ERD, Entity Relationships, Data Model Diagram]
tags: [diagram, database]
---

# Database Relationships (high level)

> Up: [[Diagrams Index]] · Explained in: [[Database Design]] · Entities: [[DOMAIN_MAP]]

```
Tenant 1───* Company 1───* Employee 1───* AttendanceRecord
                              │
                              ├───* PayrollRun 1───* LedgerEntry (append-only)
                              │            ├──* PayrollRunItem / Population (snapshot)
                              │            └──1 PayrollPayslip
                              ├───* PayrollTransaction (Addition|Deduction)
                              │            └──* PayrollTransactionAttendanceReference
                              ├───* Loan / Expense
                              └───* RequestInstance 1───1 WorkflowInstance ─* Task

PayrollDefinition (versioned) ──pins──> RuleSetVersion ──drives──> Rule Engine
WorkflowDefinition 1───* WorkflowInstance
DocumentTemplate   1───* GeneratedDocument
MasterDataObjectType 1───* MasterDataItem  (configurable catalogs)
```

Key invariants: `LedgerEntry` is [[Immutable Ledger|append-only]]; `PayrollRun` freezes its population ([[Snapshot and Versioning]]); every row is tenant-scoped ([[Multi-Tenancy]]).

## Related
[[Database Design]] · [[Financial Calculation Engine]] · [[Payroll Run State Machine]]
