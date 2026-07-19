---
title: Reproducibility
aliases: [Reproducible, Auditability, Immutability]
tags: [principle, cross-cutting, finance]
---

# Reproducibility & Auditability

> *Every calculation reproducible. Every action audited. Every policy versioned.* The core promise.
> Up: [[CLAUDE]] · Engine: [[Financial Calculation Engine]]

- **Reproducible** — same inputs → same outputs. A run pins its [[Snapshot and Versioning|definition + rule-set versions]] and freezes its population, so any historical payroll recomputes identically forever.
- **Audited** — every action carries who/when/source; the [[Immutable Ledger]] preserves full financial history; a change is done only with Serilog logging ([[CLAUDE|Definition of Done]]).
- **Immutable finance** — ledger entries appended or reversed, never edited.

This is why payroll tests require **deterministic** cases and why corrections use [[ADR-Reversal-over-Reopen|reversals, not reopens]].

## Related
[[Immutable Ledger]] · [[Snapshot and Versioning]] · [[Financial Calculation Engine]] · [[Cross-Cutting Rules]]
