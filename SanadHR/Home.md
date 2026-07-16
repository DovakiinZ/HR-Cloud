---
title: Home
aliases: [SanadHR, HR-Cloud, MOC, Map of Content, Start Here]
tags: [moc, home]
---

# 🏠 SanadHR — Knowledge Base

> **SanadHR** (سند) is a next-generation **Human Resources Operating System (HR OS)** for the Saudi market — a multi-tenant modular monolith where **[[Payroll Engine|Payroll]] is one app on top of a general [[Financial Calculation Engine]]**. Configuration-first, audit-everything, version-everything.
>
> This vault is the single, graph-connected source of truth. Every note links to its neighbours — open the **graph view** to see the whole system at once.

This is the root **Map of Content**. Start here, then follow the links.

---

## 🧭 Control Documents (read these first)

| Note | What it answers |
|---|---|
| [[CLAUDE]] | How any AI agent (and human) must work in this repo — rules, stack, Definition of Done |
| [[PROJECT_STATUS]] | The honest current state — what's live, what's mock, what's blocked |
| [[IMPLEMENTATION_STATUS]] | Per-module build status matrix (backend + frontend + tests) |
| [[ROADMAP]] | What's next, by module and by payroll sub-project |
| [[MODULE_INDEX]] | Every backend module, one line each → deep notes |
| [[DOMAIN_MAP]] | The business domain — lifecycles and rules |
| [[FEATURE_MAP]] | Cross-cutting features and where they live |
| [[DECISION_LOG]] | Architecture Decision Records (why we built it this way) |
| [[GLOSSARY]] | Domain + engineering terms (GOSI, EOS, WPS, ledger, AST…) |
| [[COMPETITORS]] | **Competitive Intelligence System** — consult before designing any feature |

---

## 🗂️ Documentation Sections

Each section has its own index note.

- 🏛️ [[Architecture Index]] — layers, engines, multi-tenancy, tech stack, deployment
- 🧩 [[MODULE_INDEX|Modules Index]] — the 17 backend modules
- 🌍 [[DOMAIN_MAP|Domain Index]] — employee / payroll / attendance / request lifecycles
- ✨ [[FEATURE_MAP|Features Index]] — payroll additions/deductions, settlement, access mgmt…
- 🛣️ [[ROADMAP|Roadmap Index]] — feature roadmap + payroll run-operations plan
- 📐 [[Specs Index]] — the Financial Engine redesign specs & plans
- ⚖️ [[DECISION_LOG|Decisions Index]] — ADRs
- 🖼️ [[Diagrams Index]] — architecture, DB, state machines, lifecycles
- 🔌 [[API Index]] — REST conventions + endpoint map
- 📖 [[GLOSSARY|Glossary Index]] — terminology
- 🔬 [[Research Index]] — open questions, context, background
- 📝 [[Changelog Index]] — schema/migration history & release notes
- 🎯 [[Competitor Index]] — competitive intelligence: 32 competitor nodes (Enterprise / MENA / Modern / UX references)

---

## ⭐ The Signature Engines

The heart of the product — reusable engines that many modules stand on:

- [[Financial Calculation Engine]] — the platform; [[Payroll Engine|Payroll]] is one app on it
- [[Immutable Ledger]] · [[Rule Engine]] · [[Formula Engine]] · [[Dependency Graph Execution]]
- [[Snapshot and Versioning]] · [[Scope Engine]] · [[Workflow Engine]] · [[Completion Effects Engine]]

---

## 🚀 Common entry points

- New here? → [[CLAUDE]] then [[Architecture Overview]]
- Working on payroll? → [[Payroll Engine]] → [[Financial Calculation Engine]] → [[Specs Index]]
- Understanding the business? → [[DOMAIN_MAP]] → [[End of Service]] (Saudi labor law)
- Deploying? → [[Deployment and Infrastructure]]

---

*Vault maintained as an Obsidian-native knowledge base. Notes never duplicate — they backlink. If a fact appears in two places, one of them is wrong.*
