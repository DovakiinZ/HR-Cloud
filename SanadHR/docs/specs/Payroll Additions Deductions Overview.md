---
title: Payroll Additions Deductions Overview
aliases: [Sub-project 2 Overview, Additions Deductions Overview]
tags: [spec, payroll, finance]
---

# Sub-project 2 — Additions/Deductions (Overview)

> Source: `docs/superpowers/specs/2026-06-30-payroll-subproject-2-additions-deductions-OVERVIEW.md` (scoping note). The philosophy change that spawned 2A/2C/2D/2E.
> Up: [[Specs Index]] · Feature: [[Payroll Additions and Deductions]]

## The core change
Make **every** payroll addition/deduction a **visible, traceable record that exists *before* approval**, replacing the fact-only model where attendance impacts were invisible rule inputs. **"No hidden deductions."**

**Why now:** sub-project 1 shipped cutoff config but couldn't *enforce* carry-over — dated transactions didn't exist. Sub-project 2 introduces them.

## Enterprise design requirements (mandatory)
Full **transaction lifecycle** (Draft…Posted/Reversed) · **posting metadata** (full chain to the ledger) · **reversal model** (never edit posted) · **payroll impact preview** · **duplicate/conflict detection** · **`IPayrollTransaction` abstraction** (add sources without touching the engine) · **transaction priority** (Gov → court orders → loans → attendance → manual → optional) · **effective dating** (business calc uses `EffectiveDate`) · **batch import** (validate→preview→approve) · **immutable posting** · **historical traceability** · **full audit & explainability**.

## The slices
- [[Subproject 2A Transaction Records]] — the record store, lifecycle, pages.
- [[Subproject 2C Consumption Posting Reversal]] — engine consumption (the real "2B").
- [[Subproject 2D Attendance Deduction Records]] — attendance → deduction records.
- [[Subproject 2E Attendance Daily Overtime Excuse]] — daily actions + overtime + excuse.

## Related
[[Payroll Additions and Deductions]] · [[Immutable Ledger]] · [[ADR-Unified-PayrollTransaction]] · [[Financial Engine Redesign Master]]
