---
title: Saudi Compliance Notes
aliases: [Saudi Compliance, GOSI, WPS, Government Integration]
tags: [research, saudi, compliance]
---

# Saudi Compliance Notes

> Context on the Saudi statutory/government surface the product targets. Terms defined in [[GLOSSARY]].
> Up: [[Research Index]] · Domain: [[End of Service]]

## Government platforms (reconciliation targets)
- **Qiwa (قوى)** — labor-market platform.
- **Mudad (مُدد)** — payroll / WPS platform.
- **GOSI (التأمينات الاجتماعية)** — social insurance; contribution deducted from pay.

The product's wedge is **reconciling payroll across these** and validating the **WPS / SIF (ملف حماية الأجور)** wage file before the bank transfer ([[Product Vision]]).

## Statutory calculations
- **End of Service** gratuity under **Labor Law Articles 84 & 85**, with scenario awards (77/80/81). Implemented — [[End of Service]] / [[Settlement Engine]].
- **GOSI deduction** — flows through the [[Payroll Engine|payroll]] fact provider; statutory *deduction packs* are on the [[ROADMAP]].

## Backend Saudi defaults
Money defaults **SAR**; Address defaults **SA**; PhoneNumber defaults **+966**. `CompanySettings` seeds SAR, Asia/Riyadh, Sunday week-start, 21 annual leave days. Hijri + Gregorian calendars; Arabic RTL throughout ([[Arabic RTL]]).

## Open items
- GOSI statutory deduction packs (planned).
- WPS/SIF file generation + bank-transfer export (planned — [[Payroll Run Operations Roadmap|exports]]).

## Related
[[End of Service]] · [[Payroll Engine]] · [[GLOSSARY]] · [[ROADMAP]]
