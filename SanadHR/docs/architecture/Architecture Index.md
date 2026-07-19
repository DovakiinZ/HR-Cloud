---
title: Architecture Index
aliases: [Architecture, Architecture Section]
tags: [index, architecture]
---

# 🏛️ Architecture Index

> The technical map of SanadHR. Start with the overview, then drill into layers, engines, and infrastructure.
> Up: [[Home]] · Business view: [[DOMAIN_MAP]] · Rules: [[CLAUDE]]

## Core

- [[Architecture Overview]] — the big picture (modular monolith, clean architecture, CQRS, event-driven)
- [[Clean Architecture Layers]] — the 5 projects and the inward-only dependency rule
- [[Tech Stack]] — authoritative technology + versions
- [[Multi-Tenancy]] — how tenant isolation works
- [[Cross-Module Integration]] — how modules talk without coupling
- [[Database Design]] — schema principles, audit fields, storage
- [[Deployment and Infrastructure]] — Azure + Vercel topology, env, gotchas

## Signature engines

- [[Financial Calculation Engine]] — the platform payroll runs on
  - [[Immutable Ledger]] · [[Rule Engine]] · [[Formula Engine]] · [[Dependency Graph Execution]] · [[Snapshot and Versioning]] · [[Scope Engine]]
- [[Workflow Engine]] — graph-based approval state machines
- [[Completion Effects Engine]] — side effects on request/workflow completion
- [[Master Data Engine]] — the generic configurable-catalog engine
- [[Settlement Engine]] — Saudi end-of-service calculation

## Cross-cutting concepts

- [[Configuration over Hardcoding]] · [[Reproducibility]] · [[Arabic RTL]] · [[TDD]]
- [[Development Standards]] (11-section feature template) · [[AGENTS Directive]] (Next.js 16 warning)

## Diagrams

- [[Diagrams Index]] → [[System Architecture Diagram]], [[Database Relationships]], [[Payroll Run State Machine]], [[Domain Lifecycle Diagrams]]
