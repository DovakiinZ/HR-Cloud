---
title: DOMAIN_MAP
aliases: [Domain Index, Domain Map, Business Domain]
tags: [index, domain]
---

# 🌍 DOMAIN_MAP — The Business Domain

> How HR actually works inside SanadHR. Read before implementing business logic.
> Up: [[Home]] · Engineering view: [[Architecture Overview]] · Rules: [[CLAUDE]]

SanadHR models the full **employee lifecycle** for a Saudi-first, Arabic-RTL, multi-tenant workforce. Every process is **configurable**, **audited**, and **versioned** ([[Cross-Cutting Rules]]).

---

## Core lifecycles

| Lifecycle | Flow | Note |
|---|---|---|
| **Employee** | Onboarding → Active → Changes → Offboarding → Settlement | [[Employee Lifecycle]] |
| **Payroll** | Inputs → Rule Evaluation → Preview/Snapshot → Approval → Ledger Post → Payslip | [[Payroll Lifecycle]] |
| **Attendance** | Capture → Validate → Aggregate → Payroll Impact | [[Attendance Lifecycle]] |
| **Request (ESS)** | Employee Request → Workflow Instance → Approval Routing → Resolution → Document/Notification | [[Request Lifecycle]] |
| **End of Service** | Termination scenario → wage/service calc → gratuity + awards → settlement expense + PDF | [[End of Service]] |

Visual versions: [[Domain Lifecycle Diagrams]].

---

## Saudi-market specifics

The domain is **Saudi-first**. Key statutory concepts (defined in [[GLOSSARY]]):

- **[[End of Service|EOS]] / نهاية الخدمة** — gratuity under Saudi Labor Law **Articles 84 & 85**, with termination scenarios (resignation, Article 77 invalid termination, Article 80 for-cause, Article 81 employer-breach resignation).
- **GOSI / التأمينات الاجتماعية** — social-insurance deduction.
- **WPS / SIF (ملف حماية الأجور)** — wage-protection file reconciled before bank transfer.
- Government platforms: **Qiwa (قوى)**, **Mudad (مُدد)**, **GOSI** — the product positions itself as a **reconciliation engine** across them.
- Currency **SAR**, timezone **Asia/Riyadh**, **Hijri + Gregorian** calendars, week starts **Sunday**, default **21** annual leave days.

---

## Cross-cutting business rules

Canonical note: [[Cross-Cutting Rules]]. In brief:

- **Multi-tenancy** — every record tenant-scoped; no cross-tenant leakage ([[Multi-Tenancy]]).
- **Configurable over hardcoded** — policies are data, not code ([[Master Data Engine]]).
- **Auditability** — every action carries who/when; financial data is immutable ([[Immutable Ledger]]).
- **Versioning** — policies & salary structures keep full history ([[Snapshot and Versioning]]).
- **Approval-driven** — payroll, leave, terminations pass through [[Workflow Engine|workflows]].

---

## Related

- Modules that realise these lifecycles: [[Employees]], [[Payroll Engine]], [[Attendance]], [[ESS]], [[Workflows]]
- Feature deep-dives: [[FEATURE_MAP]]
- The financial substrate under payroll: [[Financial Calculation Engine]]
