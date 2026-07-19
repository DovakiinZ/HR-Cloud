---
title: ROADMAP
aliases: [Roadmap Index, Roadmap]
tags: [index, roadmap]
---

# 🛣️ ROADMAP — Feature Roadmap

> Organized by module. Legend: ✅ Done · 🔧 In progress · 🗓️ Planned.
> Up: [[Home]] · State: [[PROJECT_STATUS]] · Matrix: [[IMPLEMENTATION_STATUS]] · Domain: [[DOMAIN_MAP]]
>
> The near-term payroll plan has its own detailed note: **[[Payroll Run Operations Roadmap]]** (sub-projects 2E → 3 → 4 → 5 → 6).

---

## Payroll & Financial — [[Payroll Engine]]
- ✅ Configurable salary structures / [[Payroll Types Scope Cutoff|payroll types]]
- ✅ [[Immutable Ledger|Immutable ledger-based calculations]]
- ✅ [[Formula Engine|Dynamic formula engine]] + [[Rule Engine|rule engine]]
- ✅ [[Payroll Additions and Deductions|Additions & deductions]] as records
- ✅ Payroll preview & [[Snapshot and Versioning|snapshots]]
- ✅ [[Snapshot and Versioning|Versioned payroll policies]]
- ✅ Multi-step approval ([[Payroll Run State Machine]])
- ✅ [[Attendance Payroll Impact|Attendance → deduction records]] (2D)
- 🔧 [[Subproject 2E Attendance Daily Overtime Excuse|Overtime → addition, configurable rates, excuse cancel]] (2E)
- 🗓️ Run details / quick actions (3) → [[Payroll Run Operations Roadmap]]
- 🗓️ Payslips: preview/print/download/store (4)
- 🗓️ Exports: Excel/PDF/CSV/TXT/bank file (5)
- 🗓️ Run void / amend / reissue (6)
- 🗓️ GOSI / statutory deduction packs

## Attendance & Time — [[Attendance]]
- ✅ Shift management · overtime calculation
- 🔧 Biometric machine integrations · geo-fence tracking
- 🗓️ Mobile attendance (iOS/Android) with GPS validation
- 🗓️ Attendance anomaly detection

## Loans — [[Loans]]
- ✅ Loan management core
- 🔧 Payroll deduction integration
- 🗓️ Loan approval workflow templates

## Expenses — [[Expenses]]
- ✅ Expense tracking core
- 🔧 Reimbursement → payroll/ledger flow
- 🗓️ Receipt OCR / attachment validation

## Employees (Core HR) — [[Employees]]
- ✅ Employee lifecycle · org structure & hierarchy · multi-company
- ✅ [[End of Service|End-of-service settlement]] (Saudi Articles 84/85)
- ✅ [[Termination and Restore|Termination approval + restore]]
- 🗓️ Bulk import / data-migration tooling

## Workflows & Approvals — [[Workflows]]
- ✅ [[Workflow Engine|Dynamic no-code workflow builder]] · state-machine execution
- ✅ [[Tasks|Task management]] & approval routing · [[Request Center]]
- 🗓️ Workflow analytics / SLA tracking

## Documents & Forms — [[Documents]]
- ✅ [[Document Platform|Template engine]] · RTL PDF · certificates/contracts/payslips · custom builder
- 🗓️ Digital signatures (full e-sign flow)

## Reporting & Analytics — [[Reports]] / [[Dashboards]]
- ✅ Dashboard builder + widgets · custom report builder · PDF/XLSX export · templates
- 🗓️ Scheduled report delivery

## ESS — [[ESS]]
- ✅ Employee portal · default & custom request types · workflow integration + PDF
- 🗓️ Mobile ESS app · WhatsApp / Apple Messages channel

## Notifications — [[Notifications]]
- ✅ Notification engine
- 🗓️ WhatsApp integration · Email/SMS channel templates

## Platform & Infrastructure — [[Platform]]
- ✅ [[Multi-Tenancy|Multi-tenant isolation]] · RBAC · Hangfire jobs · Serilog logging
- 🗓️ Redis caching (provision + wire) · API↔DB co-location · production hardening

---

Related: [[FEATURE_MAP]] · [[Specs Index]] · [[DECISION_LOG]]
