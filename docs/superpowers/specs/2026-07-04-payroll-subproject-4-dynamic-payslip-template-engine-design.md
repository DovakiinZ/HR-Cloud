# SP4 — Dynamic Payslip Template Engine — Design & Plan

> Part of the [final-phase master architecture](2026-07-04-payroll-final-phase-SP4-SP9-master-architecture.md).
> Owner directive: the payslip is a **template TYPE inside the existing Document Template engine**, one
> data model → many renderers, `{{Payroll.Components}}` loops dynamically, historical payslips immutable.
> Design grounded in a full map of the existing engine (`HR.Domain/Engines/Documents`,
> `Services/Documents/DocumentRenderer.cs`, `DocumentTokenResolver.cs`, `GeneratedDocument`, `StoredFile`,
> `CompanyProfile`, the `LeaveRecordService` non-request precedent, and the `DocumentDesigner` FE).

## Reuse decisions (resolving the 6 gaps)

1. **Repeating components block (gap #1).** Add ONE payslip-scoped block type `"components"` to the block
   model — NOT a general repeater engine (YAGNI). Backend: new case in `DocumentRenderer.RenderBlocks`
   that renders the run's component lines grouped **Earnings / Deductions** + a **Totals** row. Frontend:
   `"components"` in `types.ts` `BlockType` + `BLOCK_DEFS` + `newBlock` + `BlockPreview` (sample grouped
   rows) + `PropertiesPanel` (toggles: show earnings/deductions/totals, bilingual column labels).
2. **Component data channel (gap #2).** Tokens stay scalar. Add an additive optional
   `Components: IReadOnlyList<PayslipComponentLine>?` to `DocumentRenderRequest` so the `components` block
   iterates it. `PayslipComponentLine = (Code, LabelAr, LabelEn, Amount, PayComponentKind)`.
3. **Template type (gap #3).** Reuse the existing `DocumentTemplate.Module` discriminator with
   `Module="Payroll"` (least migration; matches how "Requests" works). Un-hardcode `module:"Requests"` in
   `document-templates.ts`; filter the designer list + token palette by module.
4. **Immutable archive (gap #4).** `GeneratedDocument` is metadata-only today (re-rendered live). SP4
   archives real bytes: render PDF → persist a `StoredFile` (DB `byte[]`) → `GeneratedDocument`
   `{EntityType="PayrollPayslip", EntityId=payslipId, FileUrl=/api/files/{id}, TokenValues=snapshot}`.
   **Archived bytes are the reproducibility guarantee** — editing the template later cannot change a
   payslip that's already archived. Not-yet-archived runs render live.
5. **Generation orchestration (gap #5).** Follow the `LeaveRecordService` pattern, not the request-mapping
   table. New `PayslipDocumentService` (Infrastructure): from a `PayrollPayslip` build the token dict +
   component lines, resolve the payslip template, call `IDocumentRenderer.RenderDocumentAsync`, persist
   StoredFile + GeneratedDocument. Idempotent per (payslip, template).
6. **Token namespace (gap #6).** Build `Payroll.*` + `Employee.*` + `Company.*` inline from the
   `PayrollPayslip` snapshot (immutable identity already denormalized on it) + `CompanyProfile`. Register
   the new tokens in `DocumentTokens.Catalog`/`.Sample` so they appear in the designer palette + preview.

### Reproducibility & versioning
Provenance (payslip template id + `Version` int at generation time) is stored in
`GeneratedDocument.TokenValues`. Historical immutability is guaranteed by **archiving rendered bytes**
(StoredFile), so no renderer-reads-version-snapshot machinery is needed. Re-render is only for un-archived
runs. (If per-run template pinning is later required for re-render fidelity, snapshot LayoutJson into the
existing `DocumentTemplateVersion` and record the id on the run — deferred; bytes already satisfy the rule.)

### Generation strategy
Render-on-demand + cache for preview; **bulk archive-on-Approve** (background pass over run items → one
StoredFile+GeneratedDocument each). Download/print serves the archived StoredFile when present, else renders
live. ESS employee history lists archived GeneratedDocuments where `EntityType="PayrollPayslip"`.

## Endpoints (extend `api/payroll`; permission-gated)
- `GET  runs/{id}/payslips` — paged (employee, net, archived?).
- `GET  runs/{id}/payslips/{employeeId}` — preview model (identity + grouped components + totals).
- `GET  runs/{id}/payslips/{employeeId}/pdf?print=` — bytes (archived StoredFile or live render).
- `POST runs/{id}/payslips/generate` — bulk archive.
- `GET  employees/{id}/payslips` — ESS history (self or Payroll.Payslip.View).
- `POST runs/{id}/payslips/{employeeId}/email` — reuse notification/email infra.

## Permissions
`Payroll.Payslip.View`, `Payroll.Payslip.Print`, `Payroll.Payslip.Download` + an **ESS self rule**
(an employee may fetch their own payslip without the global perm). Seed + grant to Payroll Officer / Finance
/ Employee-self, deny-wins resolver unchanged.

## Default template
Seed `DOC_PAYSLIP` (`Module="Payroll"`, `IsSystem`, Published) via a new `PayslipTemplateSeeder` mirroring
`DocumentLibrarySeeder`: branded header (logo, AR/EN name, CR, VAT, contact), employee block, the new
`components` block, totals, footer (QR, stamp, signature, generated-by/date, run number, reproducibility
line). Bilingual labels throughout.

## TDD task plan
1. **Perms** — seed `Payroll.Payslip.*` + grant migration; test seed/grant present.
2. **Component extraction** — `PayslipComponentLine` + parse `ComponentsJson` into grouped
   earnings/deductions; test totals reconcile to `GrossEarnings`/`TotalDeductions`/`NetAmount` (pure, TDD).
3. **Render channel** — `DocumentRenderRequest.Components` + `components` block in `RenderBlocks`; test the
   block emits grouped rows + totals (renderer unit or a thin projection unit).
4. **Token builder** — `Payroll.*`/`Employee.*`/`Company.*` dict from a `PayrollPayslip`; test tokens.
5. **PayslipDocumentService** — resolve template, render, persist StoredFile+GeneratedDocument (archive);
   test bytes stored + idempotent re-archive.
6. **Endpoints + DTOs** — list/detail/pdf/generate/email/ESS; wire perms.
7. **Seed `DOC_PAYSLIP`** default template; test idempotent + renders without error.
8. **FE** — payslip tab on run page + employee-profile/ESS payslips; designer `components` block + Payroll
   module type; `document-templates.ts` module un-hardcode; payroll payslip api client.
9. **Perms UI gating** + `request-center.ts` email backslash-bug fix reused for payslip email.

## Migration footprint
Only permission seed/grant rows (pattern of prior perm migrations). `GeneratedDocument`, `StoredFile`,
`DocumentTemplate`, `CompanyProfile` tables already exist — no new tables/columns required.
