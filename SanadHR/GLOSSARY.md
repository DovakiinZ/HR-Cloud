---
title: GLOSSARY
aliases: [Glossary Index, Glossary, Terminology]
tags: [index, glossary, reference]
---

# 📖 GLOSSARY

> Domain (Saudi HR) and engineering terms used across the vault. Link to a term with `[[GLOSSARY#Term]]` or its own note where one exists.
> Up: [[Home]] · Domain: [[DOMAIN_MAP]] · Engine: [[Financial Calculation Engine]]

---

## Saudi HR / Payroll domain

- **EOS — End of Service (نهاية الخدمة)** — statutory gratuity paid on termination under Saudi Labor Law **Articles 84 & 85**. See [[End of Service]].
- **Article 77 / 80 / 81** — labor-law termination scenarios: 77 = invalid termination award; 80 = for-cause dismissal (no gratuity); 81 = resignation due to employer breach. See [[End of Service]].
- **GOSI (التأمينات الاجتماعية)** — General Organization for Social Insurance; social-insurance contribution deducted from pay.
- **WPS — Wage Protection System (حماية الأجور)** — mandated wage-transfer program; salaries validated/reconciled before bank payment.
- **SIF — Salary Information File (ملف حماية الأجور)** — the WPS wage file reconciled and sent to the bank.
- **Qiwa (قوى)** — Saudi labor-market government platform.
- **Mudad (مُدد)** — Saudi payroll/WPS government platform.
- **Gratuity** — the EOS benefit amount (`gratuityAmount`).
- **Cutoff day** — day of month after which a dated transaction rolls into the next payroll period. See [[Payroll Types Scope Cutoff]].
- **DayBasis** — proration basis for daily wage: `CalendarMonth`, `Fixed30`, or `WorkingDays`.

## Financial engine

- **[[Immutable Ledger]] / FinancialLedgerEntry** — append-only atom of money movement; never edited; corrected by a **reversal** entry.
- **Reversal** — a counter-entry pointing back via `ReversesEntryId` with opposite direction so the pair nets to zero.
- **[[Rule Engine]] / RuleSet / Rule** — versioned calculation-rule library storing source + compiled **AST JSON**.
- **AST (Abstract Syntax Tree)** — parsed/compiled form of a rule expression, stored as JSON and evaluated by the [[Formula Engine]].
- **[[Dependency Graph Execution]]** — topological ordering of interdependent rules.
- **PayrollDefinition / PayrollDefinitionVersion** — the versioned payroll *policy* a run pins to; = the "Payroll Type". See [[Snapshot and Versioning]].
- **PayrollRun** — one execution of a definition over a period; governed by the [[Payroll Run State Machine]].
- **PayrollRunPopulation** — the frozen set of employees resolved for a run (snapshot).
- **PayrollTransaction** — dated, approvable addition/deduction with a `Kind` discriminator. See [[Payroll Additions and Deductions]].
- **Fact bag / PayrollFactProvider** — the raw per-employee inputs (basic, allowances, additions, deductions, GOSI, attendance aggregates) fed to the rules.
- **[[Scope Engine]]** — resolves which employees a run includes (dimension registry + pluggable providers).
- **AttendancePayrollKind** — enum `Absence / Late / Shortage / Overtime`; the engine keys on this, not on labels.

## Engineering / platform

- **Modular Monolith** — one deployable, internally split into bounded-context modules. See [[Architecture Overview]].
- **[[Clean Architecture Layers|Clean Architecture]]** — Domain → Application → Infrastructure → API; inward-only dependencies.
- **CQRS** — commands (writes) vs queries (reads), via MediatR.
- **[[Multi-Tenancy]]** — every entity tenant-scoped; app-layer isolation with global query filters.
- **[[Master Data Engine]]** — one generic `MasterDataItem` table keyed by `ObjectType` for all configurable catalogs.
- **[[Completion Effects Engine]]** — plug-in side-effect orchestrator run when a request/workflow completes.
- **[[Workflow Engine]] vs FlowBuilder** — two coexisting approval engines (graph-based vs linked-list). See [[Workflows]].
- **Deny-wins** — permission resolution where any deny overrides allows. See [[Access Management]].
- **RTL** — right-to-left Arabic layout; mandatory. See [[Arabic RTL]].

---

*Missing a term? Add it here, then backlink from wherever it's used. Never re-define a term in a module note — link to this glossary.*
