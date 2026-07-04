# SP5 — Pluggable Bank-Export + Reports Framework — Design & Plan

> Part of the [final-phase master architecture](2026-07-04-payroll-final-phase-SP4-SP9-master-architecture.md).
> Owner directive: country-agnostic, nothing hardcoded. Pipeline **Bank Profile → Field Mapper →
> Validator → Exporter → File**. Validation separated from generation. Versioned export templates. Saudi
> WPS/SIF is the FIRST registered profile, not a special case. Ready for CSV/TXT/XML/fixed-width/API
> without engine changes. This becomes the standard payroll export framework for every country.

## Layered architecture

**1. Format-agnostic dataset (pure).** `TabularDataset(Title, Columns[], Rows[])` where a column is
`TabularColumn(Key, Header, Align)` and a row is `IReadOnlyDictionary<string, object?>` keyed by column
Key. All reports and bank files project their data into this shape.

**2. Format writers (the "Exporter" layer) — DI-discovered by `ExportFormat`.**
`IExportWriter { ExportFormat Format; string ContentType; string Extension; byte[] Write(TabularDataset, ExportWriteOptions) }`.
- `CsvExportWriter` (RFC-4180 quoting), `TxtExportWriter` (delimited OR fixed-width via column widths),
  `ExcelExportWriter` (ClosedXML — reuse AttendanceExporter pattern), `XmlExportWriter`.
- New format = new writer; engine unchanged.

**3. Report providers — DI-discovered by `PayrollReportType`.**
`IPayrollReportProvider { PayrollReportType Type; Task<TabularDataset> BuildAsync(Guid runId, ct) }`.
Types: RunSummary, EmployeeDetail, Additions, Deductions, Excluded, AttendanceImpact. Each queries the
run's snapshot data (PayrollPayslips / transactions / exclusions) into a `TabularDataset`.

**4. Bank export pipeline (the WPS/SIF family). Data-driven, nothing hardcoded.**
- Canonical `BankPaymentRow(EmployeeNumber, EmployeeName, Iban, BankCode, NationalId, NetAmount, Currency)`
  built from the run's payslips + employee bank details.
- `IBankProfile { Code; DisplayName; Version; ExportFormat Format; IReadOnlyList<BankField> Fields }` —
  a profile is DATA (a versioned, ordered field list), so a new bank = a new profile instance, engine
  unchanged. `BankField(OutputKey, Header, SourceKey, Width?, Format?)` maps a canonical source field to
  an output column/position.
- `BankFieldMapper` (pure) — projects `BankPaymentRow`s through a profile's fields → a `TabularDataset`.
- `IBankExportValidator` (pure, SEPARATE from generation) — validates rows against the profile
  (IBAN present/format, NetAmount > 0, BankCode present, currency). Returns `BankValidationError[]`; the
  service refuses to generate a bank file with errors.
- **`SaudiWpsSifProfile`** = the first registered profile (SIF field set, delimited/fixed format).

**5. Orchestration.**
- `PayrollExportJob` entity: `RunId`, `Kind` (report type or bank profile code), `Format`, `Status`,
  `ArtifactStoredFileId`, `RowCount`, `RequestedByUserId`, `CreatedAt`, `Error`. Reuses `StoredFile` for
  the artifact (like SP4). Sync now; Hangfire-ready later (reuse the execution-scheduler pattern).
- `IPayrollExportService` (Application) `CreateAsync(runId, ExportRequest{ kind, format, options })` →
  resolve provider or bank pipeline → write → StoredFile → job. Implemented in the Payroll module
  (add ClosedXML there).

## Endpoints (`api/payroll`)
- `POST runs/{id}/exports` (Payroll.Export; bank kinds also require Payroll.Export.Bank) → job.
- `GET  runs/{id}/exports` (Payroll.Export) → list.
- `GET  exports/{jobId}/download` (Payroll.Export) → the StoredFile bytes.

## Permissions
`Payroll.Export` (exists) + new `Payroll.Export.Bank` (IBAN/bank files are more sensitive). Seed + grant
migration (Finance + Payroll Officer get both; deny-wins resolver unchanged).

## FE
Export dialog on the run page: pick report type + format (bank option gated by Payroll.Export.Bank),
create job, list recent exports, download via blob (reuse the SP4 blob-download pattern).

## Versioning & future-proofing
`IBankProfile.Version` + profiles-as-data satisfy "versioned export templates" and "configurable field
mapping" with code-registered profiles for v1. DB-backed, admin-editable profiles + a profile-version
table are a clean follow-up (the registry + abstraction already isolate the engine). CSV/TXT/XML/
fixed-width covered by writers; API-based bank submission = a future `IBankSubmitter` alongside writers.

## TDD task plan
1. `TabularDataset` + `CsvExportWriter` (pure, RFC-4180) — RED→GREEN.
2. `TxtExportWriter` (delimited + fixed-width) — pure.
3. Bank pipeline pure core: `BankPaymentRow`, `BankField`, `IBankProfile`, `SaudiWpsSifProfile`,
   `BankFieldMapper`, `IBankExportValidator` + Saudi validator — pure, RED→GREEN.
4. `ExcelExportWriter` (ClosedXML) + `XmlExportWriter` — reuse pattern; verify by build.
5. Report providers (RunSummary, EmployeeDetail, Additions, Deductions) — integration.
6. `PayrollExportJob` entity + migration + `Payroll.Export.Bank` perm (+ grant).
7. `IPayrollExportService` + endpoints + DI.
8. FE export dialog + api client.

## Migration footprint
One migration: `PayrollExportJob` table + `Payroll.Export.Bank` permission rows + system-role grant.
