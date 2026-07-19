---
title: Immutable Ledger
aliases: [Ledger, Financial Ledger, FinancialLedgerEntry]
tags: [architecture, engine, finance]
---

# Immutable Ledger

> The append-only financial truth. Corrections are reversing entries, never edits.
> Up: [[Financial Calculation Engine]] · Decision: [[ADR-Immutable-Ledger]]

`FinancialLedgerEntry` (`HR.Domain/Engines/Finance/Entities/`) is the immutable atom of all money movement. It is **never updated or deleted**.

- A correction is a **`Reversal`** entry pointing back via `ReversesEntryId` with **opposite `Direction`**, so the pair nets to zero.
- Fields: `SignedAmount`, `Direction`, `SourceModule`, `ComponentCode`, `Version`, `ReferenceType`/`ReferenceId`, status. `Amount` **rejects negatives** — sign is implied by entry semantics.
- Writer: `IFinancialLedger` → `FinancialLedger`; `ReverseAsync` writes the counter-entry.
- Per-transaction ledger links reuse `ReferenceType`/`ReferenceId` (e.g. `"PayrollTransaction"` + id) — no dedicated FK needed.

This is why "edit an approved payroll" is answered by a **[[ADR-Reversal-over-Reopen|reversal model]]**, not a reopen path. See [[Subproject 2C Consumption Posting Reversal]].

## Related
[[Financial Calculation Engine]] · [[Database Design]] · [[Cross-Cutting Rules]] · [[Payroll Additions and Deductions]]
