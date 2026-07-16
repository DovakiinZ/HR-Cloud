---
title: Documents
aliases: [Documents Module, HR.Modules.Documents, Document Engine]
tags: [module]
---

# Documents

> Template-driven PDF generation (QuestPDF, RTL) — the engine behind the [[Document Platform]] feature.
> Up: [[MODULE_INDEX]] · Feature: [[Document Platform]]

## Purpose
Generate branded, RTL-capable PDFs (payslips, certificates, contracts, request documents) from a JSON block-model template engine with a visual designer.

## Architecture
`HR.Modules.Documents` — `DocumentsController` (application-only module; no direct Infrastructure ref). Engine in `HR.Domain/Engines/Documents/`.

## Entities
`DocumentTemplate` (+ `Version`, `Token`), `GeneratedDocument`, `EmployeeDocument`, `CompanyBranding`, `PageTemplate`, `DocumentWorkflowLink`.

## Services
QuestPDF rendering, token engine (`TokenResolver`), page-template chrome (header/footer/margins/watermark). Known fix: logo `FitHeight→FitArea` (was 500ing).

## Events
Documents generated on request resolution / termination / payroll approval ([[Cross-Module Integration]]).

## Dependencies
[[Settings]] (company branding), [[Request Center]] (request→template mapping), [[Employees]] (settlement PDF), file storage via [[Core]] (S3/R2).

## API
`api/platform/documents`, `api/platform/page-templates`, `api/platform/tokens`. → [[API Endpoint Map]]. Frontend: `/documents`, `/settings/document-templates`, `/settings/document-branding`, `/settings/page-templates`.

## Current Status
✅ Built + deployed (Template Builder live); document platform migration applied.

## Future Work
Digital signatures (full e-sign) → [[ROADMAP]].

## Related Notes
[[Document Platform]] · [[Request Center]] · [[Settings]] · [[Arabic RTL]]
