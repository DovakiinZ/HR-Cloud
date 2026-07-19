---
title: DECISION_LOG
aliases: [Decisions Index, ADR Log, Architecture Decisions]
tags: [index, decisions, adr]
---

# ⚖️ DECISION_LOG — Architecture Decision Records

> Why SanadHR is built the way it is. Each ADR captures a decision, its context, and its consequences. Detailed records live in `docs/decisions/`.
> Up: [[Home]] · Related: [[Architecture Overview]] · [[Specs Index]]

Sourced from `docs/superpowers/specs/*` (payroll redesign) and the codebase. Status: **Accepted** unless noted.

---

## Platform-wide

| ADR | Decision | Note |
|---|---|---|
| PLAT-1 | **Modular monolith** over microservices — module isolation without distributed-systems overhead | [[ADR-Modular-Monolith]] |
| PLAT-2 | **Clean Architecture** — Domain depends on nothing; dependencies point inward | [[Clean Architecture Layers]] |
| PLAT-3 | **App-layer multi-tenancy** — single DbContext + global query filters, tenant key per entity | [[ADR-Multi-Tenancy-Strategy]] |
| PLAT-4 | **Configurable over hardcoded** — new catalogs are `MasterDataObjectType`s, never new tables | [[ADR-No-Duplicate-Fields]] |
| PLAT-5 | **Decoupling via provider/registry + DI assembly scan** — payroll never touches another module's schema | [[ADR-Provider-Registry-Pattern]] |
| PLAT-6 | **Dapper for reads, EF Core for writes** | [[Database Design]] |

## Financial Calculation Engine

| ADR | Decision | Note |
|---|---|---|
| FIN-1 | **Immutable append-only ledger** — corrections are reversing entries, never edits | [[ADR-Immutable-Ledger]] |
| FIN-2 | **Stored-AST rule engine** — rules persist source text **and** compiled AST JSON | [[ADR-Stored-AST-Rule-Engine]] |
| FIN-3 | **Dependency-ordered evaluation** — execution order from a rule dependency graph, not sequence | [[Dependency Graph Execution]] |
| FIN-4 | **Versioned immutable definitions + frozen run population** — org changes never rewrite history | [[ADR-Versioned-Definitions]] |
| FIN-5 | **Run state machine** with immutable/terminal states; invalid transitions throw | [[Payroll Run State Machine]] |
| FIN-6 | **Payroll = an app on the engine** — enrich, don't rewrite the mature engine | [[Financial Calculation Engine]] |

## Payroll operational layer (sub-projects)

| ADR | Decision | Note |
|---|---|---|
| PAY-1 | **Payroll Type = enriched `PayrollDefinition`** (+ version); no parallel "type" entity | [[Payroll Types Scope Cutoff]] |
| PAY-2 | **Pluggable Scope Engine** — dimension registry + per-module resolver strategy | [[Scope Engine]] |
| PAY-3 | **Single `PayrollTransaction` + `Kind` discriminator** (Addition/Deduction) — one lifecycle/API | [[ADR-Unified-PayrollTransaction]] |
| PAY-4 | **Reversal model instead of run-reopen** — edit an approved payroll auditably | [[ADR-Reversal-over-Reopen]] |
| PAY-5 | **No hidden deductions** — attendance penalties become visible records before approval | [[Attendance Payroll Impact]] |
| PAY-6 | **Engine keys on `AttendancePayrollKind` enum, not master-data labels** — fixed meaning vs configurable presentation | [[ADR-Attendance-Penalty-Kind]] |
| PAY-7 | **Overtime → `Kind=Addition` transaction** consumed by the existing `ADDITIONS` rule — no new rule, no double-count; **opt-in** | [[Subproject 2E Attendance Daily Overtime Excuse]] |
| PAY-8 | **Define-now / populate-later columns** in 2A so later sub-projects need no second migration on the same table | [[Subproject 2A Transaction Records]] |
| PAY-9 | **Resolve target period at run time** from EffectiveDate + cutoff — changing cutoff never strands a record | [[Subproject 2C Consumption Posting Reversal]] |

## Workflows

| ADR | Decision | Note |
|---|---|---|
| WF-1 | **Two workflow engines coexist by isolation** — FlowBuilder (`flow_*`, linked-list) vs graph-based [[Workflow Engine]] | [[Workflows]] |
| WF-2 | **Soft pointers, not FKs, for step transitions** — free rewiring, no EF cascade-cycle constraints | [[Workflows]] |
| WF-3 | **Step handlers = Strategy + Open/Closed** — new step type = new handler + one DI line | [[Workflow Engine]] |

---

## How decisions are made

Each payroll sub-project runs a full **brainstorm → spec → plan → build (subagents) → verify → ship** cycle. The "why" is preserved in the [[Specs Index|design specs]]; this log is the durable index. When a decision is reversed, mark it **Superseded** and link the replacement.
