---
title: Arabic RTL
aliases: [RTL, Arabic, Internationalization, i18n]
tags: [principle, frontend, cross-cutting]
---

# Arabic RTL

> RTL support is **mandatory, not optional**. SanadHR is Saudi-first and Arabic-first.
> Up: [[CLAUDE]] · Design: [[Design System]]

- Root layout: `<html lang="ar" dir="rtl">`; sidebar on the **end/right**; `font-sans` = *Thmanyah Sans*.
- Use **logical properties** (`ms-*/me-*/ps-*/pe-*/start-*/end-*`, `text-start/end`); mirror chevron/arrow icons.
- Numbers/currency: locale `ar-SA`, currency **SAR**, western numerals; **Hijri + Gregorian** calendars.
- Enum values render as Arabic labels (Gender ذكر/أنثى, Status نشط/موقوف/…, ContractType دوام كامل/جزئي) via AutoMapper on the backend and Arabic union types on the frontend (`src/types`).
- App title: *سند — نظام إدارة الموارد البشرية*. Documents (payslips, certificates) render RTL via QuestPDF ([[Documents]]).

## Related
[[Design System]] · [[Documents]] · [[CLAUDE]]
