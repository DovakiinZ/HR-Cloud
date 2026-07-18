# Report Field Registry Adapter (Phase 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** A read-only Report Field Registry that turns trusted business field-keys into executable report descriptors (object Guid + column + auto-join path + operators), sourced from the Semantic Catalog + ObjectCatalog. Backend-only. No UI, no execution wiring, no migration. Engine preserved.

**Architecture:** `ISemanticCatalogProvider` (subjects/labels/roles/ReportEnabled) + `IObjectCatalogService` (dataType/references/display columns) + `IReportObjectIdResolver` (object code → ObjectDefinition Guid) → `ReportFieldRegistryAdapter` → read-only API. Validates + self-heals (exclude + log + health). Nothing in the existing engine changes.

**Tech Stack:** .NET 8 (xUnit + FluentAssertions). No new deps, no migration.

## Global Constraints
- **Do NOT modify** `ReportSqlBuilder`, `ReportObjectResolver`, `ReportExecutionService`, report access/export, saved reports, tenant isolation, permissions, or legacy ObjectDefinition resolution.
- Semantic Catalog is the single source of truth — changes are **additive** (`ReportEnabled` + more curated fields). No second registry, no duplicate metadata.
- Contract + interfaces in `HR.Application`; adapter + EF resolver + API in `HR.Modules.Platform`.
- Registry APIs under `/api/platform/reports/*`; responses use the `OkResponse(...)` **ApiResponse envelope** (frontend apiFetch requires it). Health gated on **`Platform.Reports.Delete`** (admin) — never reachable via `Platform.Reports.View`.
- Invalid/unresolved fields are **excluded + logged + in health**, never throw.
- Arabic labels are UTF-8 string literals; return exactly (no corruption).
- The only raw tokens crossing the API are `Key`/`ObjectCode`/`PropertyPath` (opaque; future builders pass them back, never displayed). Frontend never sends entity names/columns/SQL/joins.

## Confirmed facts
- `SemanticField(string ObjectCode, string FieldCode, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, string GroupCode, string? Icon, IReadOnlyList<string> Keywords, SemanticFieldRole Role, bool DefaultVisible)` in `HR.Application/SemanticCatalog/Contracts/SemanticContracts.cs`. `SemanticFieldRole { Dimension, Measure, Filter, Identifier }`.
- `ISemanticCatalogProvider` (HR.Application.SemanticCatalog): `GetDomains(ctx)`, `GetObjects(ctx, domain?)`, `GetObject(ctx, code)`, `GetMetrics(...)`, `Search(...)`, `GetHealth()`; `CatalogQueryContext(IReadOnlyCollection<string> Permissions)`. `SemanticObject` has `ObjectCode`, `DomainCode`, `Fields` (List<SemanticField>). `SemanticDomain(Code, NameAr, NameEn, DescriptionAr, DescriptionEn, Icon, SortOrder)`.
- `IObjectCatalogService` (HR.Modules.Platform.Services.Catalog): `GetObject(string code) : CatalogObjectDto?`. `CatalogObjectDto { Code, NameEn, NameAr, Fields:List<CatalogFieldDto> }`. `CatalogFieldDto { Code, NameEn, NameAr, FieldType, IsMeasure, IsGroupable, IsFilterable, IsDate, IsReference, ReferenceObjectCode, Options }`.
- `ObjectDefinition : TenantEntity` (`HR.Domain.Engines.ObjectRegistry`) has `Code`; DbSet `_db.ObjectDefinitions` (`ApplicationDbContext`, `HR.Infrastructure.Persistence`).
- `ReportsController` (route `api/platform/reports`, `BaseApiController`, `OkResponse` helper) — inject `ICurrentUserService` (`HR.Application.Common.Interfaces`, `.Permissions : IReadOnlyList<string>`) if not present.
- `[RequirePermission(...)]` from `HR.Api.Filters` (any-of, params string[]) — used by other Platform controllers.
- Display-column priority (from ObjectCatalogService.PickDisplay): `NameAr, Name, NameEn, TitleAr, Title, DisplayName, FullName, FirstNameAr+LastNameAr, FirstName+LastName, Code, Number, EmployeeNumber`.

---

## File map
**Create (HR.Application/Reports/Registry/):** `ReportRegistryContracts.cs`, `IReportFieldRegistry.cs`, `IReportObjectIdResolver.cs`.
**Create (HR.Modules/Platform/Services/Reports/):** `ReportRegistryHelpers.cs` (DisplayColumnPicker + ReportOperators), `ReportObjectIdResolver.cs` (EF), `ReportFieldRegistryAdapter.cs`.
**Modify:** `HR.Application/SemanticCatalog/Contracts/SemanticContracts.cs` (+`ReportEnabled`), `HR.Modules/Platform/Services/SemanticCatalog/CatalogRegistry.cs` (mark + add report fields), `HR.Modules/Platform/Controllers/ReportsController.cs` (+4 endpoints), `HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` (register 2 services).
**Tests (HR.Modules.Platform.Tests/Reports/):** `ReportRegistryHelpersTests.cs`, `ReportFieldRegistryAdapterTests.cs`, `CatalogReportFieldsTests.cs`.

---

## Task 1: Contract + interfaces (HR.Application)

**Files:** Create `ReportRegistryContracts.cs`, `IReportFieldRegistry.cs`, `IReportObjectIdResolver.cs` under `backend/src/HR.Application/Reports/Registry/`.

- [ ] **Step 1: `ReportRegistryContracts.cs`**
```csharp
namespace HR.Application.Reports.Registry;

public sealed record ReportSubjectDescriptor(string Key, string LabelAr, string LabelEn, string Icon, int SortOrder);

public sealed record ReportJoinStep(string SourceObjectCode, string TargetObjectCode, string JoinField);

public sealed record ReportFieldDescriptor(
    string Key, string LabelAr, string LabelEn, string Subject, string Group, string DataType,
    Guid ObjectDefinitionId, string ObjectCode, string PropertyPath,
    IReadOnlyList<ReportJoinStep> JoinPath, IReadOnlyList<string> AllowedOperators,
    bool Filterable, bool Sortable, bool Groupable, bool Aggregatable, string? DefaultAggregation,
    bool IsDefault, int DisplayOrder, string? FormatPattern, string RequiredPermission);

public sealed record ReportResolveResult(
    IReadOnlyList<ReportFieldDescriptor> Fields,
    IReadOnlyList<ReportJoinStep> RequiredJoins,
    IReadOnlyList<string> UnknownKeys);

public sealed record ReportRegistryExclusion(string Key, string Reason);
public sealed record ReportRegistryHealth(
    int VisibleSubjects, int VisibleFields, int ExcludedFields,
    IReadOnlyList<ReportRegistryExclusion> Exclusions);

public sealed record ReportRegistryContext(IReadOnlyCollection<string> Permissions);
```

- [ ] **Step 2: `IReportFieldRegistry.cs`**
```csharp
namespace HR.Application.Reports.Registry;

public interface IReportFieldRegistry
{
    IReadOnlyList<ReportSubjectDescriptor> GetSubjects(ReportRegistryContext ctx);
    IReadOnlyList<ReportFieldDescriptor> GetFields(ReportRegistryContext ctx, string subject);
    ReportFieldDescriptor? GetField(ReportRegistryContext ctx, string key);
    ReportResolveResult Resolve(ReportRegistryContext ctx, IReadOnlyCollection<string> keys);
    ReportRegistryHealth GetHealth();
}
```

- [ ] **Step 3: `IReportObjectIdResolver.cs`** (object code → ObjectDefinition Guid; EF impl in Task 3, fakeable in tests)
```csharp
namespace HR.Application.Reports.Registry;

/// <summary>Maps a catalog object code (e.g. "Employee") to its ObjectDefinition Guid (the engine's identifier).</summary>
public interface IReportObjectIdResolver
{
    Guid? ResolveId(string objectCode);
}
```

- [ ] **Step 4: Build** `dotnet build backend/src/HR.Application/HR.Application.csproj -v q` → 0 errors.
- [ ] **Step 5: Commit** `git add backend/src/HR.Application/Reports/ && git commit -m "feat(reports): Report Field Registry contract + interfaces"`

---

## Task 2: SemanticField.ReportEnabled + curated report fields

**Files:** Modify `SemanticContracts.cs`, `CatalogRegistry.cs`; Test `backend/tests/HR.Modules.Platform.Tests/Reports/CatalogReportFieldsTests.cs`.

- [ ] **Step 1: Add `ReportEnabled` to `SemanticField`** (append LAST — backward-compatible; existing `new SemanticField(...)` calls keep compiling with the default):
```csharp
public sealed record SemanticField(
    string ObjectCode, string FieldCode,
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string GroupCode, string? Icon, IReadOnlyList<string> Keywords,
    SemanticFieldRole Role, bool DefaultVisible, bool ReportEnabled = false);
```

- [ ] **Step 2: Curate report fields in `CatalogRegistry.cs`.** For each subject's primary object, ensure the reportable OWN columns and the reference columns exist as `SemanticField`s marked `ReportEnabled: true`, with correct Arabic/English labels + a field-group. Confirm each column name against the live entities (open the entity if unsure) — anything wrong self-excludes at runtime + shows in health, but curate accurately. Cover at minimum:
  - **Employee** (subject `employees`): `EmployeeNumber`(الرقم الوظيفي), `FirstNameAr`(الاسم الأول), `LastNameAr`(اسم العائلة), `Status`(الحالة), `HireDate`(تاريخ التعيين), `ContractEndDate`(نهاية العقد), `BasicSalary`(الراتب الأساسي, Measure), and reference fields `DepartmentId`(القسم), `BranchId`(الفرع), `JobTitleId`(المسمى الوظيفي), `ManagerId`(المدير المباشر), `NationalityId`(الجنسية) — all `ReportEnabled: true`.
  - **AttendanceRecord** (`attendance`): `Date`(التاريخ), `CheckIn`(وقت الدخول), `CheckOut`(وقت الخروج), `WorkedMinutes`(دقائق العمل, Measure), `LateMinutes`(دقائق التأخير, Measure), `ShortageMinutes`(دقائق الانصراف المبكر, Measure), `OvertimeMinutes`(دقائق العمل الإضافي, Measure), `RequiredMinutes`(الدقائق المطلوبة, Measure), `Status`(حالة الحضور), `ShiftId`(الوردية), `EmployeeId`(reference → employee display).
  - **PayrollPayslip** (`payroll`): `EmployeeName`(اسم الموظف), `EmployeeNumber`(الرقم الوظيفي), `GrossEarnings`(إجمالي الاستحقاقات, Measure), `TotalDeductions`(إجمالي الخصومات, Measure), `NetAmount`(صافي الراتب, Measure), `Currency`(العملة).
  - **LeaveBalance** (`leaves`): `Year`(السنة), `EntitledDays`(الأيام المستحقة, Measure), `UsedDays`(الأيام المستخدمة, Measure), `CarriedForwardDays`(المرحّلة, Measure), `LeaveTypeId`(نوع الإجازة), `EmployeeId`(reference).
  - **RequestInstance** (`requests`): `RequestNumber`(رقم الطلب), `Status`(الحالة), `SubmittedAt`(تاريخ التقديم), `RequestTypeId`(نوع الطلب), `EmployeeId`(reference).
  - (Expenses/Loans/Documents subjects: mark their primary objects' key fields ReportEnabled if the objects exist in the catalog; if not, they self-exclude — acceptable this phase.)
  Reuse existing SemanticFields where present (just add `ReportEnabled: true`); add the missing ones.

- [ ] **Step 3: Write the Arabic-integrity + report-field test** `CatalogReportFieldsTests.cs`:
```csharp
using System.Linq;
using FluentAssertions;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class CatalogReportFieldsTests
{
    private static System.Collections.Generic.IEnumerable<HR.Application.SemanticCatalog.Contracts.SemanticField> AllFields()
        => CatalogRegistry.Objects.SelectMany(o => o.Fields);

    [Fact]
    public void Some_fields_are_report_enabled()
        => AllFields().Any(f => f.ReportEnabled).Should().BeTrue();

    [Fact]
    public void Manager_reference_label_is_correct_arabic()
    {
        var mgr = AllFields().FirstOrDefault(f => f.ObjectCode == "Employee" && f.FieldCode == "ManagerId");
        mgr.Should().NotBeNull();
        mgr!.NameAr.Should().Be("المدير المباشر");        // exact — proves no corruption/reversal
        mgr.ReportEnabled.Should().BeTrue();
    }

    [Fact]
    public void Department_reference_label_is_correct_arabic()
        => AllFields().First(f => f.ObjectCode == "Employee" && f.FieldCode == "DepartmentId").NameAr.Should().Be("القسم");
}
```
> If the existing Department label is "الإدارة" not "القسم", set the curated label to "القسم" per the owner's §7 list (القسم), and assert that.

- [ ] **Step 4: Run** `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~CatalogReportFieldsTests` → PASS. Also confirm existing catalog/dashboard tests still pass (the `ReportEnabled` default doesn't break them): `--filter FullyQualifiedName~CatalogRegistryTests` and `~CodeDefinedSemanticCatalog`.
- [ ] **Step 5: Commit** `git add backend/src/HR.Application/SemanticCatalog/Contracts/SemanticContracts.cs backend/src/HR.Modules/Platform/Services/SemanticCatalog/CatalogRegistry.cs backend/tests/HR.Modules.Platform.Tests/Reports/CatalogReportFieldsTests.cs && git commit -m "feat(reports): mark + add ReportEnabled catalog fields (Arabic labels)"`

---

## Task 3: Registry helpers + EF resolver + adapter (TDD)

**Files:** Create `ReportRegistryHelpers.cs`, `ReportObjectIdResolver.cs`, `ReportFieldRegistryAdapter.cs` under `backend/src/HR.Modules/Platform/Services/Reports/`; Tests `ReportRegistryHelpersTests.cs`, `ReportFieldRegistryAdapterTests.cs`.

**Interfaces:** Consumes Task-1 contracts, `ISemanticCatalogProvider`, `IObjectCatalogService`, `IReportObjectIdResolver`. Produces `ReportFieldRegistryAdapter : IReportFieldRegistry`, `ReportObjectIdResolver : IReportObjectIdResolver`, static helpers.

- [ ] **Step 1: Pure helpers + failing tests.** `ReportRegistryHelpers.cs`:
```csharp
using HR.Application.Reports.Registry;

namespace HR.Modules.Platform.Services.Reports;

public static class ReportRegistryHelpers
{
    private static readonly string[] DisplayPriority =
        { "NameAr","Name","NameEn","TitleAr","Title","DisplayName","FullName","Code","Number","EmployeeNumber","EmployeeName" };

    /// <summary>Pick a target object's display column from its available columns, by priority.</summary>
    public static string? PickDisplayColumn(IReadOnlyCollection<string> columns)
    {
        foreach (var p in DisplayPriority)
            foreach (var c in columns)
                if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return c;
        return columns.FirstOrDefault();
    }

    /// <summary>Allowed filter operators for a catalog data type (business-friendly, engine-supported).</summary>
    public static IReadOnlyList<string> OperatorsFor(string dataType) => dataType switch
    {
        "Number" or "Decimal" or "Currency" or "Percentage" =>
            new[]{ "Equals","NotEquals","GreaterThan","GreaterThanOrEqual","LessThan","LessThanOrEqual","Between" },
        "Date" or "DateTime" => new[]{ "Equals","Between","GreaterThan","LessThan" },
        "Boolean" => new[]{ "Equals" },
        "Reference" or "Enum" => new[]{ "Equals","NotEquals","In" },
        _ => new[]{ "Equals","NotEquals","Contains","StartsWith","EndsWith","In" }, // Text/Guid/other
    };
}
```
`ReportRegistryHelpersTests.cs`: assert `PickDisplayColumn(["Id","NameAr","Code"]) == "NameAr"`, falls back to first when no priority match; `OperatorsFor("Number")` contains Between and not Contains; `OperatorsFor("Text")` contains Contains. Run → RED → the helper makes them GREEN.

- [ ] **Step 2: EF resolver** `ReportObjectIdResolver.cs`:
```csharp
using HR.Application.Reports.Registry;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportObjectIdResolver : IReportObjectIdResolver
{
    private readonly Dictionary<string, Guid> _map;
    public ReportObjectIdResolver(ApplicationDbContext db)
        => _map = db.ObjectDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Select(o => new { o.Code, o.Id }).ToList()
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
    public Guid? ResolveId(string objectCode) => _map.TryGetValue(objectCode, out var id) ? id : null;
}
```
> `ObjectDefinition` is a TenantEntity; `IgnoreQueryFilters` reads across the registry (codes are global identifiers). Confirm `ObjectDefinition.Id`/`.Code` exist.

- [ ] **Step 3: Write the failing adapter tests** `ReportFieldRegistryAdapterTests.cs`. Use fakes: a fake `ISemanticCatalogProvider` returning domains + objects with `ReportEnabled` fields; a fake `IObjectCatalogService` returning `CatalogObjectDto`s (with reference fields + target objects' columns); a fake `IReportObjectIdResolver` mapping codes → Guids. Cover the spec's 8 assertions:
```csharp
// (representative — the implementer writes all 8)
[Fact] public void Own_field_maps_to_object_and_column_no_join() { /* attendance.checkIn → AttendanceRecord Guid, "CheckIn", JoinPath empty */ }
[Fact] public void Related_field_maps_to_target_display_and_join_path() { /* employee.departmentName → Department Guid, "NameAr", JoinPath [Employee→Department via DepartmentId] */ }
[Fact] public void Invalid_field_is_excluded_and_in_health() { /* a ReportEnabled field whose column is absent → not in GetFields, present in GetHealth().Exclusions with reason */ }
[Fact] public void Permission_filter_hides_payroll_without_permission() { /* GetFields(ctx w/o Payroll.View, "payroll") excludes; health counts it */ }
[Fact] public void Resolve_returns_descriptors_joins_and_unknown_keys() { }
[Fact] public void Operators_derive_from_datatype() { }
```

- [ ] **Step 4: Implement `ReportFieldRegistryAdapter.cs`.** Build the descriptor set ONCE in the constructor (over `ISemanticCatalogProvider.GetObjects` + per-object `IObjectCatalogService.GetObject` + `IReportObjectIdResolver`), recording exclusions:
  - For each `SemanticObject`, for each `SemanticField` with `ReportEnabled`:
    - Resolve `objectDefinitionId = _ids.ResolveId(field.ObjectCode)` — null → exclude(`"object '<code>' has no ObjectDefinition"`).
    - Look up the live catalog object + field (`IObjectCatalogService.GetObject(field.ObjectCode)` → `.Fields.FirstOrDefault(Code==field.FieldCode)`) — missing → exclude(`"field '<col>' not on '<object>'"`).
    - **Own (non-reference) field** → descriptor: `ObjectCode=field.ObjectCode`, `PropertyPath=field.FieldCode`, `JoinPath=[]`, dataType from catalog field, operators via helper, aggregatable if `IsMeasure`, subject = the domain, group = field.GroupCode, labels from the SemanticField, permission from the subject→perm map, key = `"{subject}.{camelCase(fieldCode)}"` (or a curated key — see below).
    - **Reference field** (`catalogField.IsReference`, `ReferenceObjectCode` set) → produce a RELATED-DISPLAY descriptor: resolve target catalog object; `displayCol = PickDisplayColumn(target.Fields.Select(c=>c.Code))`; `targetGuid = _ids.ResolveId(ReferenceObjectCode)` (null/target-missing → exclude with reason); `PropertyPath=displayCol`, `ObjectCode=ReferenceObjectCode`, `ObjectDefinitionId=targetGuid`, `JoinPath` = the FK chain from the **subject's primary object** to the target (see join-path resolution below). Label from the SemanticField (e.g. "القسم"). dataType `Text`, operators for Text, `Aggregatable=false`, `Groupable=true`, `Filterable=true` (semantic capability; engine wiring is Phase 4). key = `"{subject}.{referenceName}Name"` (referenceName = FieldCode minus trailing `Id`, camelCased; e.g. DepartmentId → departmentName).
  - **Join-path resolution** (`ResolveJoinPath(primaryObjectCode, targetObjectCode)`): BFS/walk over `IObjectCatalogService` references starting from the subject's primary object; each hop where object X has a reference field `FkId` → `ReferenceObjectCode Y` yields a `ReportJoinStep(X, Y, FkId)`. Stop at the target. If a field's own object == primary, path is direct; if the field's object is reached via the primary's references (e.g. attendance→employee→department), chain the steps. If no path resolves → exclude the field with reason `"no relationship path <primary>→<target>"`. Cap depth (e.g. 3) to avoid cycles.
  - **Dedup keys**: if two fields produce the same key, keep the first, exclude the rest (`"duplicate key"`).
  - `GetSubjects` = domains that have ≥1 permission-visible field. `GetFields(subject)` = visible fields for that subject, ordered by DisplayOrder. `GetField(key)`. `Resolve(keys)` = matched descriptors + deduped union of their JoinPath steps + unknown keys. `GetHealth()` = counts + all exclusions (ignores permissions). Log a one-line summary + each exclusion at Debug in the ctor.
  > Key generation: to keep keys stable + business-friendly, generate `"{subject}.{camelCase(fieldCode without Id)}"`. Own fields: `attendance.checkIn`; references: `employee.departmentName`. Ensure determinism.

- [ ] **Step 5: Run** all Task-3 tests (`--filter FullyQualifiedName~ReportFieldRegistry` and `~ReportRegistryHelpers`) → GREEN.
- [ ] **Step 6: Commit** `git add backend/src/HR.Modules/Platform/Services/Reports/ReportRegistryHelpers.cs backend/src/HR.Modules/Platform/Services/Reports/ReportObjectIdResolver.cs backend/src/HR.Modules/Platform/Services/Reports/ReportFieldRegistryAdapter.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportRegistryHelpersTests.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportFieldRegistryAdapterTests.cs && git commit -m "feat(reports): ReportFieldRegistryAdapter (subjects/fields/resolve/health)"`

---

## Task 4: API endpoints + DI

**Files:** Modify `ReportsController.cs`, `DependencyInjection.cs`.

- [ ] **Step 1: Register services** in `AddPlatformModule` (scoped — `ReportObjectIdResolver` reads DB; adapter reads catalogs):
```csharp
services.AddScoped<HR.Application.Reports.Registry.IReportObjectIdResolver,
    HR.Modules.Platform.Services.Reports.ReportObjectIdResolver>();
services.AddScoped<HR.Application.Reports.Registry.IReportFieldRegistry,
    HR.Modules.Platform.Services.Reports.ReportFieldRegistryAdapter>();
```

- [ ] **Step 2: Add endpoints to `ReportsController`.** Inject `IReportFieldRegistry` + `ICurrentUserService` into the ctor (append; keep existing). Use `OkResponse(...)`; build `ReportRegistryContext` from `_user.Permissions`:
```csharp
private HR.Application.Reports.Registry.ReportRegistryContext RegCtx => new(_user.Permissions);

[HttpGet("subjects")]
[RequirePermission("Platform.Reports.View")]
public IActionResult GetReportSubjects() => OkResponse(_registry.GetSubjects(RegCtx));

[HttpGet("subjects/{subject}/fields")]
[RequirePermission("Platform.Reports.View")]
public IActionResult GetReportSubjectFields(string subject) => OkResponse(_registry.GetFields(RegCtx, subject));

[HttpGet("fields/{key}")]
[RequirePermission("Platform.Reports.View")]
public IActionResult GetReportField(string key)
    => _registry.GetField(RegCtx, key) is { } f ? OkResponse(f) : NotFound(ApiResponse.Fail($"Field '{key}' not found"));

[HttpGet("registry/health")]
[RequirePermission("Platform.Reports.Delete")]     // admin-only; NOT reachable via Reports.View
public IActionResult GetRegistryHealth() => OkResponse(_registry.GetHealth());
```
> `OkResponse` returns `ActionResult<ApiResponse<T>>`; if the existing actions use `IActionResult`, either match their style or type these as `ActionResult<ApiResponse<...>>`. Mirror the controller's existing action signatures/return exactly. `ApiResponse` from `HR.Application.Common.Models`.
> Route order: ensure `subjects` / `fields/{key}` don't collide with an existing `{id}` route — `{id}` routes use `[HttpGet("{id:guid}")]` (Guid-constrained), so `subjects`/`fields/{key}` (string) won't clash. Verify the existing `{id}` route has the `:guid` constraint; if not, place the new literal routes before it.

- [ ] **Step 3: Build** `dotnet build backend/src/HR.Api/HR.Api.csproj -v q` → 0 errors.
- [ ] **Step 4: Commit** `git add backend/src/HR.Modules/Platform/Controllers/ReportsController.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs && git commit -m "feat(reports): registry API (subjects/fields/field/health) + DI"`

---

## Task 5: Build + test gate
- [ ] `dotnet build backend/HR.sln -v q` → 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --nologo` → all pass (new registry tests + existing unaffected). Commit if incidental.

---

## Self-Review
- Semantic Catalog = single source of truth, additive `ReportEnabled` + curated fields → Task 2. ✅
- Adapter converts field-keys → executable descriptors (object Guid, column, join path, operators) → Task 3. ✅
- Reuse ObjectCatalog references for join paths + display columns → Task 3. ✅
- Read-only APIs subjects/fields/field → Task 4; health admin-only (`Reports.Delete`) → Task 4. ✅
- Invalid fields excluded + logged + in health, never throw → Task 3. ✅
- Permission-filtered fields → Task 3/4. ✅
- Engine/SqlBuilder/saved reports/legacy flow untouched → no task touches them. ✅
- Arabic labels correct → Task 2 integrity test. ✅
- No UI, no execution wiring, no migration → scope respected. ✅

**Type consistency:** `ReportFieldDescriptor`/`ReportJoinStep`/`ReportRegistryContext` identical across Tasks 1,3,4. `IReportFieldRegistry` methods match the controller calls (Task 4). `IReportObjectIdResolver.ResolveId(code)` consistent (Tasks 1,3). Permission strings (`Platform.Reports.View/Delete`, subject perms) consistent.
