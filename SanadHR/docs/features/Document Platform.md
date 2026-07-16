---
title: Document Platform
aliases: [Document Templates, Template Builder, Document Generation]
tags: [feature, documents]
---

# Document Platform

> Enterprise document generation: a JSON block model + visual designer, page-template chrome, branding, token engine, and RTL PDF printing.
> Up: [[FEATURE_MAP]] · Module: [[Documents]]

## What it is
A Template Builder where documents are JSON blocks rendered via QuestPDF. `PageTemplate` supplies reusable chrome (header/footer/margins/watermark); `CompanyBranding` supplies logo/colors; a token engine (`TokenResolver`) fills entity data. A request→template mapping engine links request types to print templates with triggers.

## Capabilities
- 9-template starter library; certificates, contracts, payslips, request documents.
- View / download / print / email.
- Known fix: logo `FitHeight → FitArea` (was 500ing).

## Related
[[Documents]] · [[Request Center]] · [[Settings]] · [[Arabic RTL]] · [[Master Data Engine]]
