---
title: End of Service
aliases: [EOS, Settlement, End-of-Service Gratuity, نهاية الخدمة]
tags: [domain, saudi, finance]
---

# End of Service (نهاية الخدمة)

> Saudi statutory end-of-service settlement. Business rules here; the math engine is [[Settlement Engine]].
> Up: [[DOMAIN_MAP]] · Module: [[Employees]] · Glossary: [[GLOSSARY]]

When an employee is offboarded, SanadHR computes the **EOS gratuity** and any scenario-specific awards under **Saudi Labor Law Articles 84 & 85**, then creates a settlement expense and generates a settlement PDF.

## Termination scenarios (`TerminationScenario`)

| Scenario | Arabic | Effect |
|---|---|---|
| `NormalEmployerTermination` | إنهاء من صاحب العمل (سبب مشروع) | standard gratuity |
| `NormalResignation` | استقالة عادية | resignation gratuity scale |
| `Article77InvalidTermination` | فصل غير مشروع (مادة 77) | + Article 77 award |
| `Article80ForCause` | فصل لسبب مشروع بلا مكافأة (مادة 80) | no gratuity |
| `Article81EmployerBreachResignation` | ترك العمل لإخلال صاحب العمل (مادة 81) | full gratuity |

Contract term: `Indefinite` (غير محدد المدة) / `FixedTerm` (محدد المدة).

## Settlement result

`monthlyWage`, `dailyWage`, `serviceYears`, `effectiveServiceDays`, `unpaidLeaveDays`, `gratuityAmount`, `article77Award`, `noticeCompensation`, `totalAward`, + itemized `lines` (labelEn/labelAr/articleRef/amount).

Endpoints: `POST /api/employees/{id}/settlement/preview`, `POST /api/employees/{id}/terminate`. Frontend: `/employees/[id]/settlement`, `/employees/terminations`.

Known fix: settlement 500 from non-UTC date normalization (commit a8f5e85).

## Related
[[Settlement Engine]] · [[Employees]] · [[Termination and Restore]] · [[Employee Lifecycle]] · [[GLOSSARY]]
