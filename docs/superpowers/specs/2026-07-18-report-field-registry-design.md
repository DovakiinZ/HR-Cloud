# Report Field Registry Adapter (Reports Simplification — Phase 1) — Design

**Date:** 2026-07-18
**Status:** Approved (owner decisions locked). Phase 1 of a 6-phase Reports simplification program.
**Related:** [[semantic-catalog]], [[reports-engine-r1]], [[dashboard-builder-redesign]]

## Program context (why Phase 1 exists)

The Reports module (entities, execution engine, builder, viewer, export, scheduling) is complete and MUST be preserved. The goal of the program is to make reports simple for HR users (business subjects + friendly fields, no joins/codes/SQL) while keeping the advanced engine. Phase 1 builds the **foundation only**: a Report Field Registry that turns trusted **business field keys** into everything the existing engine needs — with **no UI, no execution wiring, no migration** yet.

**Owner decisions (binding):**
1. Extend the existing **Semantic Catalog** as the single source of truth — additive only, no second registry, no duplicate metadata. Report-usable fields are flagged with a **`ReportEnabled`** capability.
2. Report registry APIs live under `/api/platform/reports/{subjects, subjects/{subject}/fields, fields/{key}}`.
3. `/api/platform/reports/registry/health` is **admin-only** — gated on `Platform.Reports.Delete` (NOT reachable via `Platform.Reports.View`).
4. Pipeline: `Semantic Catalog → ReportFieldRegistryAdapter → existing ReportObjectResolver → existing ReportSqlBuilder`. Do NOT modify `ReportSqlBuilder`, report execution, saved reports, tenant isolation, permissions, or legacy ObjectDefinition resolution.
5. Frontend/future builder send ONLY trusted business field keys — never entity names, columns, SQL, or arbitrary joins.
6. Invalid/unresolved fields are excluded from the API, logged, surfaced in the health endpoint, and never break the module.
7. Arabic labels stored + returned correctly (e.g. القسم، المدير المباشر، الحضور والانصراف، المسمى الوظيفي) — no reversed/corrupted text.

## Non-goals (Phase 1)
- No simplified UI / no changes to the existing builder or viewer.
- No wiring of the registry into report creation/execution (Phase 3/4).
- No DB migration (this phase adds no columns/tables).
- No change to `ReportSqlBuilder`/`ReportObjectResolver`/`ReportExecutionService`/access/export.

## Architecture

```
Semantic Catalog  (subjects=domains, curated field labels/roles/groups, ReportEnabled)  ┐
IObjectCatalogService (dataType, FK references, display columns)                         ├─► ReportFieldRegistryAdapter ─► read-only registry API
_db.ObjectDefinitions (object code → Guid, the engine's identifier)                      ┘        │ Resolve(keys) → { ObjectDefinitionId, propertyPath, joinPath[], operators }
                                                                                                  ▼  (consumed by the EXISTING engine in later phases — unchanged here)
                                                                                          ReportObjectResolver → ReportSqlBuilder
```

- Contract + `IReportFieldRegistry` in `HR.Application/Reports/Registry/`.
- `ReportFieldRegistryAdapter` in `HR.Modules/Platform/Services/Reports/`. Depends on `ISemanticCatalogProvider`, `IObjectCatalogService`, `ApplicationDbContext` (read `ObjectDefinitions` for code→Guid), `ILogger`. Scoped (matches `IObjectCatalogService`).
- Semantic Catalog extension: `SemanticField.ReportEnabled` (additive bool) + curated report fields in `CatalogRegistry`.

## The contract

`HR.Application/Reports/Registry/ReportRegistryContracts.cs`:

```csharp
public sealed record ReportSubjectDescriptor(
    string Key, string LabelAr, string LabelEn, string Icon, int SortOrder);

public sealed record ReportJoinStep(
    string SourceObjectCode, string TargetObjectCode, string JoinField); // FK column on source → target PK

public sealed record ReportFieldDescriptor(
    string Key,                 // business key, e.g. "attendance.checkIn", "employee.departmentName"
    string LabelAr, string LabelEn,
    string Subject,             // subject/domain key
    string Group,               // field-group key (business grouping)
    string DataType,            // Text|Number|Decimal|Currency|Date|DateTime|Boolean|Reference|Enum
    Guid ObjectDefinitionId,    // the engine's Guid for the SOURCE object of this field
    string ObjectCode,          // that object's code
    string PropertyPath,        // the column on ObjectCode (e.g. "CheckIn", "NameAr")
    IReadOnlyList<ReportJoinStep> JoinPath, // empty for own fields; the FK chain for related-display fields
    IReadOnlyList<string> AllowedOperators, // e.g. ["Equals","Between","GreaterThan",...]
    bool Filterable, bool Sortable, bool Groupable, bool Aggregatable,
    string? DefaultAggregation, // "Sum"|"Average"|... when Aggregatable
    bool IsDefault, int DisplayOrder,
    string? FormatPattern,
    string RequiredPermission);

public sealed record ReportRegistryHealth(
    int VisibleSubjects, int VisibleFields, int ExcludedFields,
    IReadOnlyList<ReportRegistryExclusion> Exclusions);
public sealed record ReportRegistryExclusion(string Key, string Reason);

public sealed record ReportRegistryContext(IReadOnlyCollection<string> Permissions);
```

`HR.Application/Reports/Registry/IReportFieldRegistry.cs`:

```csharp
public interface IReportFieldRegistry
{
    IReadOnlyList<ReportSubjectDescriptor> GetSubjects(ReportRegistryContext ctx);
    IReadOnlyList<ReportFieldDescriptor> GetFields(ReportRegistryContext ctx, string subject);
    ReportFieldDescriptor? GetField(ReportRegistryContext ctx, string key);
    // Bridge for later phases (Phase 3/4): resolve a set of keys to descriptors + the DISTINCT joins needed.
    ReportResolveResult Resolve(ReportRegistryContext ctx, IReadOnlyCollection<string> keys);
    ReportRegistryHealth GetHealth(); // ignores permissions (admin diagnostic)
}

public sealed record ReportResolveResult(
    IReadOnlyList<ReportFieldDescriptor> Fields,
    IReadOnlyList<ReportJoinStep> RequiredJoins,   // deduped union of all fields' JoinPath steps
    IReadOnlyList<string> UnknownKeys);
```

## How the adapter builds descriptors

Built once (cached) and validated against the live catalogs. Two field kinds per subject:

1. **Own fields** — a `SemanticField` with `ReportEnabled = true` whose `ObjectCode` = the subject's primary object. Descriptor: `ObjectDefinitionId` = that object's Guid (`_db.ObjectDefinitions` by code), `PropertyPath` = `FieldCode`, `JoinPath = []`. Key = the field's business key.
2. **Related-display fields** — a `ReportEnabled` reference field (e.g. `Employee.DepartmentId`, `IsReference=true`, `ReferenceObjectCode="Department"` from `IObjectCatalogService`). Instead of exposing the Guid, the descriptor points at the **target object's display column** (its `PickDisplay` result — e.g. `Department.NameAr`), `ObjectDefinitionId` = target object's Guid, `PropertyPath` = target display column, `JoinPath` = the FK chain from the subject's primary object to the target (walked via `IObjectCatalogService` references; e.g. `attendance.departmentName` → `[AttendanceRecord→Employee via EmployeeId, Employee→Department via DepartmentId]`). Label = curated (e.g. "القسم" / "Department").

- **Labels/groups/roles**: from the Semantic Catalog (curated), humanized fallback only if missing.
- **DataType / references / display columns**: from `IObjectCatalogService`.
- **AllowedOperators**: derived from `DataType` — e.g. Number/Decimal/Currency → `[Equals,NotEquals,GreaterThan,GreaterThanOrEqual,LessThan,LessThanOrEqual,Between]`; Text → `[Equals,NotEquals,Contains,StartsWith,EndsWith,In]`; Date/DateTime → `[Equals,Between,GreaterThan,LessThan]`; Boolean → `[Equals]`; Reference/Enum → `[Equals,NotEquals,In]`.
- **Aggregatable / DefaultAggregation**: Number/Decimal/Currency measure fields → aggregatable, default `Sum`.
- **RequiredPermission**: per subject → the module view permission (employees→`Employees.View`, attendance→`Attendance.View`, payroll→`Payroll.View`, leaves→`Leaves.View`, requests→`Requests.View`, expenses→`Payroll.View`, loans→`Payroll.View`, documents→`Employees.View`). Curated per subject as a small explicit map; the implementation confirms each string against the seeded permission set and defaults an unmapped subject to `Platform.Reports.View`.

## Semantic Catalog extension (additive, single source of truth)

- Add `bool ReportEnabled = false` to the `SemanticField` record (last positional arg → backward-compatible; dashboard consumers ignore it).
- In `CatalogRegistry`, mark the reportable fields `ReportEnabled = true` and **add the missing curated fields** to cover the program's subject field lists (attendance check-in/out, worked/late/early/overtime minutes, status, shift, punch location, policy; employee number/name/hire/contract/status/job-title/branch/department/manager; payroll gross/net/deductions/currency; leave entitled/used/carried/remaining/type; etc.). Reference fields that should surface as related-display fields (DepartmentId, BranchId, JobTitleId, ManagerId, LeaveTypeId, …) are also marked `ReportEnabled`.
- **Arabic labels** are stored as UTF-8 string literals in the C# registry (as today — the catalog already returns correct Arabic, verified live). A test asserts an exact round-trip (e.g. `"المدير المباشر"`, `"القسم"`).

## Read-only API

Add to `ReportsController` (route `api/platform/reports`), building `ReportRegistryContext` from `ICurrentUserService.Permissions`:

| Method | Route | Gate | Returns |
|---|---|---|---|
| GET | `/subjects` | `Platform.Reports.View` | `ReportSubjectDescriptor[]` (subjects with ≥1 visible field) |
| GET | `/subjects/{subject}/fields` | `Platform.Reports.View` | `ReportFieldDescriptor[]` (permission-filtered) |
| GET | `/fields/{key}` | `Platform.Reports.View` | `ReportFieldDescriptor` (404 if unknown/hidden) |
| GET | `/registry/health` | **`Platform.Reports.Delete`** (admin; NOT View) | `ReportRegistryHealth` |

Responses use the standard `ApiResponse` envelope (`OkResponse(...)`) — the frontend `apiFetch` requires it (lesson from [[semantic-catalog]]). All human-facing text is Ar/En; the only raw tokens are `Key`/`ObjectCode`/`PropertyPath` (opaque, passed back by future builders, never displayed).

## Validation & self-healing

At build time (constructor), for every candidate field:
- Duplicate key → keep the first, exclude the rest, log + record exclusion.
- Object code not in `_db.ObjectDefinitions` (no Guid) → exclude + record.
- Own field column not on the live object (`IObjectCatalogService`) → exclude + record.
- Related field whose reference chain can't be resolved to a join (missing FK/target) → exclude + record.
Nothing throws. Consumer endpoints omit excluded fields; `GetHealth()` returns counts + every exclusion with a reason. A one-line summary is logged at startup; each exclusion at Debug.

## Permission filtering
`GetSubjects`/`GetFields`/`GetField`/`Resolve` drop fields whose `RequiredPermission` isn't in `ctx.Permissions`. Subjects with zero visible fields are hidden. `GetHealth` ignores permissions (admin sees everything).

## Testing (TDD, DB-free with fakes)
Adapter tests inject a fake `ISemanticCatalogProvider`, a fake `IObjectCatalogService`, and an in-memory `ObjectDefinitions` set (EF InMemory or a fake), asserting:
1. Subjects derive from domains that have ≥1 ReportEnabled field.
2. Own field → descriptor with correct `ObjectDefinitionId`/`PropertyPath`/empty `JoinPath`.
3. Related-display field (e.g. `attendance.departmentName`) → target object's Guid + display column + the correct multi-step `JoinPath`.
4. `AllowedOperators` correct per dataType; measure fields aggregatable with `Sum`.
5. Invalid field (missing column / broken reference / dup key) → excluded from `GetFields` AND present in `GetHealth().Exclusions` with the reason; no throw.
6. Permission filter: a payroll field hidden without `Payroll.View`, shown with it; `GetHealth` counts it regardless.
7. `Resolve(keys)` returns descriptors + the deduped `RequiredJoins` union + `UnknownKeys` for bad keys.
8. **Arabic integrity**: `GetField("employee.managerName").LabelAr == "المدير المباشر"` (exact) and other known labels — proves no corruption.
Plus: solution build + full `HR.Modules.Platform.Tests` green (existing dashboard/catalog/report tests unaffected by the additive `ReportEnabled`).

## Known limitations (carried to later phases)
- The current engine (`ReportObjectResolver`) does not filter/sort on **joined-object fields** (R1 constraint). The registry still describes related-display fields as `Filterable`/`Sortable` per their data type (semantic capability); **wiring these into execution and lifting the joined-filter limit is Phase 4**. Phase 1 only produces descriptors — it changes no execution behavior, so no report can misbehave from this.
- `Resolve` is provided but not yet consumed (Phase 3/4 will feed it to the existing add-field/add-relationship path or a create-from-keys command).

## Deliverables at completion
Reused files/services · modified files · new files · API endpoints · tests added · registry validation results (health output) · unresolved limitations.
