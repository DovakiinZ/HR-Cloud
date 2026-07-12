# Reports Engine — Phase 1: Execution Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the metadata-driven backend that turns a persisted `ReportDefinition` into a sorted, multi-level-grouped, aggregated tabular result across joined objects with computed fields, exposed via `POST /api/platform/reports/{id}/run`.

**Architecture:** A thin orchestrator (`ReportExecutionService`) resolves the definition's object Guids to physical metadata through the ObjectRegistry, validates every identifier against the live `IObjectCatalogService` (injection gate), delegates SQL construction to a **pure** `ReportSqlBuilder`, executes via ADO (mirroring `WidgetDataService`), evaluates computed fields with the existing `ExpressionEvaluator`, and shapes rows into groups/aggregates with a **pure** `ReportRowShaper`. Pure components are unit-tested; the ADO path gets an integration test.

**Tech Stack:** .NET 8, EF Core 8 (PostgreSQL, raw ADO for dynamic SQL), MediatR, AutoMapper, xUnit + FluentAssertions, existing `HR.Domain.Engines.Finance.Expressions` AST engine.

## Global Constraints

- **No hardcoded report logic.** Objects, fields, joins, filters, formulas, sorts, groups come only from metadata resolved at runtime.
- **Injection gate:** every table/column/FK name must resolve in `IObjectCatalogService`; reject otherwise. All values are bound parameters (`@p0`, `@p1`, …). Never string-interpolate an untrusted identifier or value.
- **Automatic scoping:** always inject `TenantId = <current>` and `IsDeleted = false` predicates for objects that have those columns (`ResolvedObject.HasTenant` / `HasSoftDelete`).
- **Reuse, don't duplicate:** identifier quoting via `Q(id) => "\"" + id.Replace("\"","\"\"") + "\""`; parameter accumulation via a `Params` helper identical in behavior to `WidgetDataService`. Do NOT create a new export abstraction — export is a later phase and reuses `IExportWriter`.
- **Aggregations supported:** `Count, Sum, Average, Min, Max` (from `HR.Domain.Enums.AggregationType`; `Percentage`/`DistinctCount` out of scope for reports R1).
- **Row cap:** materialization is bounded by a configurable cap (default 5000); when hit, `ReportResult.Truncated = true`.

---

## File Structure

**New backend files (all under `backend/src/HR.Modules/Platform/Services/Reports/`):**
- `ReportModels.cs` — `ReportResult`, `ReportColumn`, `ReportRow`, `ReportGroup`, `ReportQueryModel`, `ReportJoinModel`, `ReportColumnModel`, `ReportFilterModel`, `ReportSortModel` (pure DTOs/plan objects).
- `ReportAccessResolver.cs` — pure read/edit visibility resolution.
- `ComputedFieldEvaluator.cs` — formula-string → `Expr` AST → per-row value via `ExpressionEvaluator`; report `FunctionRegistry`.
- `ReportSqlBuilder.cs` — pure: `ReportQueryModel` → `(string sql, IReadOnlyList<object?> parameters)`.
- `ReportRowShaper.cs` — pure: flat rows + grouping/sort/aggregate config → `ReportResult`.
- `IReportObjectResolver.cs` / `ReportObjectResolver.cs` — bridge report Guids → `ResolvedObject` via ObjectRegistry + catalog validation.
- `IReportExecutionService.cs` / `ReportExecutionService.cs` — orchestrator + ADO execution.

**Modified backend files:**
- `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs` — add `POST {id}/run`.
- `backend/src/HR.Modules/Platform/Commands/Reports/ReportCommands.cs` — add `RunReportQuery` (in Queries file, see Task 8).
- `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs` — add `RunReportQuery` + handler.
- `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` — register the new services.

**New test project:** `backend/tests/HR.Modules.Platform.Tests/` (xUnit + FluentAssertions), with:
- `ReportAccessResolverTests.cs`, `ComputedFieldEvaluatorTests.cs`, `ReportSqlBuilderTests.cs`, `ReportRowShaperTests.cs`.

---

### Task 0: Scaffold the Platform test project

**Files:**
- Create: `backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
- Create: `backend/tests/HR.Modules.Platform.Tests/Reports/.gitkeep`

**Interfaces:**
- Produces: a runnable xUnit project referencing `HR.Modules` and `HR.Domain`, so later tasks can add test classes.

- [ ] **Step 1: Copy the csproj shape from an existing test project**

Read `backend/tests/HR.Modules.Employees.Tests/HR.Modules.Employees.Tests.csproj` and mirror its `<PropertyGroup>` and package versions. Create the new csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <!-- Match versions EXACTLY to HR.Modules.Employees.Tests.csproj -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="__COPY_FROM_EMPLOYEES__" />
    <PackageReference Include="xunit" Version="__COPY_FROM_EMPLOYEES__" />
    <PackageReference Include="xunit.runner.visualstudio" Version="__COPY_FROM_EMPLOYEES__" />
    <PackageReference Include="FluentAssertions" Version="__COPY_FROM_EMPLOYEES__" />
    <PackageReference Include="Moq" Version="__COPY_FROM_EMPLOYEES__" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\HR.Modules\HR.Modules.csproj" />
    <ProjectReference Include="..\..\src\HR.Domain\HR.Domain.csproj" />
    <ProjectReference Include="..\..\src\HR.Application\HR.Application.csproj" />
  </ItemGroup>
</Project>
```

> If `HR.Modules` is split into multiple csproj files, reference the one containing `HR.Modules.Platform` (check `backend/src/HR.Modules/`). Replace every `__COPY_FROM_EMPLOYEES__` with the actual version string.

- [ ] **Step 2: Add project to the solution**

Run: `dotnet sln backend/HR.sln add backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: "Project ... added to the solution." (Confirm the solution file name with `ls backend/*.sln` first.)

- [ ] **Step 3: Verify it builds and runs (zero tests)**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: build succeeds, "No test is available" or 0 passed/0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/tests/HR.Modules.Platform.Tests backend/*.sln
git commit -m "test(reports): scaffold HR.Modules.Platform.Tests project"
```

---

### Task 1: Result & plan models

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportModels.cs`

**Interfaces:**
- Produces (consumed by Tasks 2–8):
  - `ReportColumn { string Code; string Label; string Type; bool IsMeasure; AggregationType? Aggregation; string? FormatPattern; }`
  - `ReportRow` = `Dictionary<string, object?>` (alias: `sealed class ReportRow : Dictionary<string,object?> {}`)
  - `ReportGroup { string FieldCode; object? Key; string Label; List<ReportGroup> SubGroups; List<ReportRow> Rows; Dictionary<string,double> Aggregates; long Count; }`
  - `ReportResult { string ReportCode; List<ReportColumn> Columns; List<ReportGroup> Groups; List<ReportRow> Rows; Dictionary<string,double> GrandTotals; long TotalCount; int Page; int PageSize; bool Truncated; }`
  - Plan objects: `ReportQueryModel { ResolvedObject Primary; List<ReportJoinModel> Joins; List<ReportColumnModel> Columns; List<ReportFilterModel> Filters; List<ReportSortModel> Sorts; bool HasTenant; }`
  - `ReportJoinModel { string Alias; ResolvedObject Target; string SourceAlias; string SourceColumn; string TargetKeyColumn; string JoinType; }`
  - `ReportColumnModel { string Alias; string TableAlias; ResolvedField Field; string OutputCode; }` (only object/relationship columns reach SQL; computed columns are handled post-materialization)
  - `ReportFilterModel { string TableAlias; ResolvedField Field; ReportFilterOperator Operator; string? Value; string? ValueTo; }`
  - `ReportSortModel { string TableAlias; ResolvedField Field; SortDirection Direction; }`

- [ ] **Step 1: Write the models file**

```csharp
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Catalog;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportRow : Dictionary<string, object?>
{
    public ReportRow() : base(StringComparer.OrdinalIgnoreCase) { }
    public ReportRow(IDictionary<string, object?> src) : base(src, StringComparer.OrdinalIgnoreCase) { }
}

public sealed class ReportColumn
{
    public string Code { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Type { get; set; } = "Text";
    public bool IsMeasure { get; set; }
    public AggregationType? Aggregation { get; set; }
    public string? FormatPattern { get; set; }
}

public sealed class ReportGroup
{
    public string FieldCode { get; set; } = null!;
    public object? Key { get; set; }
    public string Label { get; set; } = "";
    public List<ReportGroup> SubGroups { get; set; } = new();
    public List<ReportRow> Rows { get; set; } = new();
    public Dictionary<string, double> Aggregates { get; set; } = new();
    public long Count { get; set; }
}

public sealed class ReportResult
{
    public string ReportCode { get; set; } = null!;
    public List<ReportColumn> Columns { get; set; } = new();
    public List<ReportGroup> Groups { get; set; } = new();   // populated when the report has groupings
    public List<ReportRow> Rows { get; set; } = new();        // flat page when no groupings
    public Dictionary<string, double> GrandTotals { get; set; } = new();
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool Truncated { get; set; }
}

// ── Resolved plan (built by ReportObjectResolver, consumed by ReportSqlBuilder) ──

public sealed class ReportQueryModel
{
    public ResolvedObject Primary { get; set; } = null!;
    public string PrimaryAlias { get; set; } = "t0";
    public List<ReportJoinModel> Joins { get; set; } = new();
    public List<ReportColumnModel> Columns { get; set; } = new();
    public List<ReportFilterModel> Filters { get; set; } = new();
    public List<ReportSortModel> Sorts { get; set; } = new();
}

public sealed class ReportJoinModel
{
    public string Alias { get; set; } = null!;
    public ResolvedObject Target { get; set; } = null!;
    public string SourceAlias { get; set; } = null!;
    public string SourceColumn { get; set; } = null!;   // FK column on the source
    public string TargetKeyColumn { get; set; } = "Id";
    public string JoinType { get; set; } = "Inner";     // Inner|Left|Right
}

public sealed class ReportColumnModel
{
    public string TableAlias { get; set; } = null!;
    public ResolvedField Field { get; set; } = null!;
    public string OutputCode { get; set; } = null!;     // unique per SELECT item
}

public sealed class ReportFilterModel
{
    public string TableAlias { get; set; } = null!;
    public ResolvedField Field { get; set; } = null!;
    public ReportFilterOperator Operator { get; set; }
    public string? Value { get; set; }
    public string? ValueTo { get; set; }
}

public sealed class ReportSortModel
{
    public string TableAlias { get; set; } = null!;
    public ResolvedField Field { get; set; } = null!;
    public SortDirection Direction { get; set; }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/HR.Modules/HR.Modules.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportModels.cs
git commit -m "feat(reports): result and resolved-plan models"
```

---

### Task 2: ReportAccessResolver (pure visibility logic)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportAccessResolver.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportAccessResolverTests.cs`

**Interfaces:**
- Produces:
  - `ReportAccessContext { Guid UserId; Guid? DepartmentId; IReadOnlySet<Guid> RoleIds; }`
  - `static class ReportAccessResolver`
    - `bool CanRead(ReportDefinition report, IReadOnlyList<ReportShare> shares, ReportAccessContext ctx)`
    - `bool CanEdit(ReportDefinition report, IReadOnlyList<ReportShare> shares, ReportAccessContext ctx)`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportAccessResolverTests
{
    private static ReportDefinition Report(Guid owner, ReportScope scope) =>
        new() { Id = Guid.NewGuid(), OwnerId = owner, Scope = scope };

    private static ReportAccessContext Ctx(Guid user, Guid? dept = null, params Guid[] roles) =>
        new() { UserId = user, DepartmentId = dept, RoleIds = new HashSet<Guid>(roles) };

    [Fact]
    public void Owner_can_read_and_edit_private_report()
    {
        var me = Guid.NewGuid();
        var r = Report(me, ReportScope.Personal);
        ReportAccessResolver.CanRead(r, Array.Empty<ReportShare>(), Ctx(me)).Should().BeTrue();
        ReportAccessResolver.CanEdit(r, Array.Empty<ReportShare>(), Ctx(me)).Should().BeTrue();
    }

    [Fact]
    public void Stranger_cannot_read_private_report()
    {
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        ReportAccessResolver.CanRead(r, Array.Empty<ReportShare>(), Ctx(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public void Anyone_can_read_company_public_report_but_not_edit()
    {
        var stranger = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Company);
        ReportAccessResolver.CanRead(r, Array.Empty<ReportShare>(), Ctx(stranger)).Should().BeTrue();
        ReportAccessResolver.CanEdit(r, Array.Empty<ReportShare>(), Ctx(stranger)).Should().BeFalse();
    }

    [Fact]
    public void User_share_grants_read_and_edit_respects_CanEdit()
    {
        var me = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        var shares = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithUserId = me, CanEdit = true } };
        ReportAccessResolver.CanRead(r, shares, Ctx(me)).Should().BeTrue();
        ReportAccessResolver.CanEdit(r, shares, Ctx(me)).Should().BeTrue();

        var readonlyShare = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithUserId = me, CanEdit = false } };
        ReportAccessResolver.CanEdit(r, readonlyShare, Ctx(me)).Should().BeFalse();
    }

    [Fact]
    public void Role_share_grants_read()
    {
        var role = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        var shares = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithRoleId = role } };
        ReportAccessResolver.CanRead(r, shares, Ctx(Guid.NewGuid(), null, role)).Should().BeTrue();
    }

    [Fact]
    public void Department_share_grants_read()
    {
        var dept = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        var shares = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithDepartmentId = dept } };
        ReportAccessResolver.CanRead(r, shares, Ctx(Guid.NewGuid(), dept)).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ReportAccessResolverTests`
Expected: FAIL — `ReportAccessResolver` / `ReportAccessContext` do not exist.

- [ ] **Step 3: Implement**

```csharp
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportAccessContext
{
    public Guid UserId { get; init; }
    public Guid? DepartmentId { get; init; }
    public IReadOnlySet<Guid> RoleIds { get; init; } = new HashSet<Guid>();
}

/// <summary>Pure visibility resolution. Read = owner OR company-scope OR a matching share.
/// Edit = owner OR a share with CanEdit. No DB access here.</summary>
public static class ReportAccessResolver
{
    public static bool CanRead(ReportDefinition report, IReadOnlyList<ReportShare> shares, ReportAccessContext ctx)
    {
        if (report.OwnerId == ctx.UserId) return true;
        if (report.Scope == ReportScope.Company) return true;
        return shares.Any(s => Matches(s, ctx));
    }

    public static bool CanEdit(ReportDefinition report, IReadOnlyList<ReportShare> shares, ReportAccessContext ctx)
    {
        if (report.OwnerId == ctx.UserId) return true;
        return shares.Any(s => s.CanEdit && Matches(s, ctx));
    }

    private static bool Matches(ReportShare s, ReportAccessContext ctx)
        => (s.SharedWithUserId is { } u && u == ctx.UserId)
        || (s.SharedWithRoleId is { } r && ctx.RoleIds.Contains(r))
        || (s.SharedWithDepartmentId is { } d && ctx.DepartmentId == d);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ReportAccessResolverTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportAccessResolver.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportAccessResolverTests.cs
git commit -m "feat(reports): pure access resolver with tests"
```

---

### Task 3: ComputedFieldEvaluator (formula → AST → per-row value)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ComputedFieldEvaluator.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ComputedFieldEvaluatorTests.cs`

**Interfaces:**
- Consumes: `HR.Domain.Engines.Finance.Expressions` — `ExpressionEvaluator`, `Expr`, `RuleValue`, `IEvaluationContext`, `FunctionRegistry`. **Before implementing, read `backend/src/HR.Domain/Engines/Finance/Expressions/` in full** to confirm the exact type names for building a context, constructing `RuleValue` (`RuleValue.Number`/`.Text`/`.Bool`), the `FunctionRegistry.CreateDefault()` + registration API, and whether an expression parser exists (`AstJson` deserializes a stored AST; if no string parser exists, computed fields consume AST JSON directly — the builder UI produces the AST). Adjust the code below to the real API discovered there.
- Produces:
  - `sealed class ComputedFieldEvaluator`
    - ctor `(FunctionRegistry? functions = null)` — defaults to `ReportFunctions()`
    - `object? Evaluate(Expr ast, IReadOnlyDictionary<string, object?> row)`
  - `static FunctionRegistry ReportFunctions()` — default registry + `age`, `yearsBetween`, `now`, `today`, `concat`, `coalesce`, `round`.

- [ ] **Step 1: Write the failing tests** (adjust `RuleValue` factory calls to the real API found in Step 0 reading)

```csharp
using FluentAssertions;
using HR.Domain.Engines.Finance.Expressions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ComputedFieldEvaluatorTests
{
    private readonly ComputedFieldEvaluator _eval = new();

    private static Expr Var(string n) => new VariableExpr(n);
    private static Expr Num(decimal n) => new LiteralExpr(RuleValue.Number(n));

    [Fact]
    public void Evaluates_arithmetic_over_row_fields()
    {
        // basicSalary - basicSalary * gosiRate
        var ast = new BinaryExpr(BinaryOp.Subtract, Var("basicSalary"),
            new BinaryExpr(BinaryOp.Multiply, Var("basicSalary"), Var("gosiRate")));
        var row = new Dictionary<string, object?> { ["basicSalary"] = 10000m, ["gosiRate"] = 0.09m };
        var result = _eval.Evaluate(ast, row);
        Convert.ToDecimal(result).Should().Be(9100m);
    }

    [Fact]
    public void Concat_builds_full_name()
    {
        var ast = new FunctionCallExpr("concat", new List<Expr>
        {
            Var("firstName"), new LiteralExpr(RuleValue.Text(" ")), Var("lastName")
        });
        var row = new Dictionary<string, object?> { ["firstName"] = "Sara", ["lastName"] = "Ali" };
        _eval.Evaluate(ast, row).Should().Be("Sara Ali");
    }

    [Fact]
    public void YearsBetween_computes_service_years()
    {
        var ast = new FunctionCallExpr("yearsBetween", new List<Expr>
        {
            Var("hireDate"), new FunctionCallExpr("today", new List<Expr>())
        });
        var row = new Dictionary<string, object?> { ["hireDate"] = new DateTime(2020, 7, 12) };
        var years = Convert.ToInt32(_eval.Evaluate(ast, row));
        years.Should().BeGreaterThanOrEqualTo(5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ComputedFieldEvaluatorTests`
Expected: FAIL — `ComputedFieldEvaluator` does not exist (and possibly compile errors on `RuleValue`/`Expr` factory names — fix those to match the real API from your Step 0 reading before proceeding).

- [ ] **Step 3: Implement** (a row-backed `IEvaluationContext` + report helper functions; align factory/registration names to the real engine API)

```csharp
using HR.Domain.Engines.Finance.Expressions;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Evaluates a computed-field AST against a materialized report row, reusing the
/// Finance expression engine. Pure and deterministic. Variables resolve to the row's field values.</summary>
public sealed class ComputedFieldEvaluator
{
    private readonly ExpressionEvaluator _evaluator;

    public ComputedFieldEvaluator(FunctionRegistry? functions = null)
        => _evaluator = new ExpressionEvaluator(functions ?? ReportFunctions());

    public object? Evaluate(Expr ast, IReadOnlyDictionary<string, object?> row)
    {
        var value = _evaluator.Evaluate(ast, new RowContext(row));
        return value.ToClr();   // if RuleValue has no ToClr(), map by kind — see note below
    }

    /// <summary>Default finance functions + report helpers. Register using the SAME API
    /// FunctionRegistry.CreateDefault() uses (confirm signature: name + Func&lt;RuleValue[],RuleValue&gt;).</summary>
    public static FunctionRegistry ReportFunctions()
    {
        var reg = FunctionRegistry.CreateDefault();
        reg.Register("today", _ => RuleValue.Date(DateTime.UtcNow.Date));
        reg.Register("now", _ => RuleValue.Date(DateTime.UtcNow));
        reg.Register("age", a => RuleValue.Number(YearsBetween(a[0].AsDate(), DateTime.UtcNow)));
        reg.Register("yearsBetween", a => RuleValue.Number(YearsBetween(a[0].AsDate(), a[1].AsDate())));
        reg.Register("concat", a => RuleValue.Text(string.Concat(a.Select(v => v.AsText()))));
        reg.Register("coalesce", a => a.FirstOrDefault(v => !v.IsNull) ?? RuleValue.Null());
        reg.Register("round", a => RuleValue.Number(Math.Round(a[0].AsNumber(), (int)a[1].AsNumber())));
        return reg;
    }

    private static decimal YearsBetween(DateTime from, DateTime to)
    {
        var years = to.Year - from.Year;
        if (to < from.AddYears(years)) years--;
        return years;
    }

    private sealed class RowContext : IEvaluationContext
    {
        private readonly IReadOnlyDictionary<string, object?> _row;
        public RowContext(IReadOnlyDictionary<string, object?> row) => _row = row;

        public bool TryResolve(string name, out RuleValue value)
        {
            if (_row.TryGetValue(name, out var raw)) { value = ToRuleValue(raw); return true; }
            value = RuleValue.Null();
            return false;
        }

        private static RuleValue ToRuleValue(object? raw) => raw switch
        {
            null => RuleValue.Null(),
            decimal d => RuleValue.Number(d),
            int i => RuleValue.Number(i),
            long l => RuleValue.Number(l),
            double db => RuleValue.Number((decimal)db),
            bool b => RuleValue.Bool(b),
            DateTime dt => RuleValue.Date(dt),
            _ => RuleValue.Text(raw.ToString() ?? ""),
        };
    }
}
```

> **API-alignment note:** the exact `RuleValue` factory names (`Number/Text/Bool/Date/Null`), accessors (`AsNumber/AsText/AsDate/IsNull`), a `ToClr()` helper, `IEvaluationContext.TryResolve` signature, and `FunctionRegistry.Register` signature MUST be confirmed against the real files in Task 3 Step 0. If `RuleValue` has no `Date` kind, store dates as text/number and adjust `age`/`yearsBetween` accordingly. Do not invent members — match what exists.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ComputedFieldEvaluatorTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ComputedFieldEvaluator.cs backend/tests/HR.Modules.Platform.Tests/Reports/ComputedFieldEvaluatorTests.cs
git commit -m "feat(reports): computed-field evaluator reusing expression engine"
```

---

### Task 4: ReportSqlBuilder (pure SELECT/JOIN/WHERE/ORDER BY + params)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportSqlBuilder.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportSqlBuilderTests.cs`

**Interfaces:**
- Consumes: `ReportQueryModel`, `ReportJoinModel`, `ReportColumnModel`, `ReportFilterModel`, `ReportSortModel` (Task 1); `ResolvedObject`/`ResolvedField`/`FieldKind` (catalog); `ReportFilterOperator`, `SortDirection` (enums); tenant id passed in.
- Produces:
  - `static class ReportSqlBuilder`
    - `static (string Sql, IReadOnlyList<object?> Parameters) Build(ReportQueryModel model, Guid tenantId, int rowCap)`
  - Behavior: `SELECT <alias.col AS OutputCode>, … FROM <primary table> t0 [JOIN … ] WHERE <tenant/soft-delete + filters> ORDER BY <sorts> LIMIT rowCap+1` (the +1 lets the caller detect truncation).

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportSqlBuilderTests
{
    private static ResolvedField Field(string code, string col, FieldKind kind = FieldKind.Text) =>
        new() { Code = code, ColumnName = col, Kind = kind, ClrType = kind == FieldKind.Number ? typeof(int) : typeof(string) };

    private static ResolvedObject Employee() => new()
    {
        Code = "Employee", TableName = "Employees", HasTenant = true, HasSoftDelete = true, KeyColumn = "Id",
        Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase)
        {
            ["FullName"] = Field("FullName", "FullName"),
            ["Salary"] = Field("Salary", "BasicSalary", FieldKind.Number),
            ["DepartmentId"] = Field("DepartmentId", "DepartmentId", FieldKind.Guid),
        }
    };

    private static ReportQueryModel BaseModel() => new()
    {
        Primary = Employee(), PrimaryAlias = "t0",
        Columns = { new ReportColumnModel { TableAlias = "t0", Field = Employee().Fields["FullName"], OutputCode = "c0" } },
    };

    [Fact]
    public void Selects_columns_with_tenant_and_softdelete_scope()
    {
        var (sql, ps) = ReportSqlBuilder.Build(BaseModel(), Guid.NewGuid(), rowCap: 100);
        sql.Should().Contain("SELECT t0.\"FullName\" AS \"c0\"");
        sql.Should().Contain("FROM \"Employees\" t0");
        sql.Should().Contain("t0.\"TenantId\" = @p0");
        sql.Should().Contain("t0.\"IsDeleted\" = false");
        sql.Should().Contain("LIMIT 101");   // rowCap + 1
        ps.Should().HaveCount(1);            // tenant id
    }

    [Fact]
    public void Emits_inner_join_on_validated_fk()
    {
        var dept = new ResolvedObject { Code = "Department", TableName = "Departments", KeyColumn = "Id",
            Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase) { ["Name"] = Field("Name", "Name") } };
        var m = BaseModel();
        m.Joins.Add(new ReportJoinModel { Alias = "t1", Target = dept, SourceAlias = "t0", SourceColumn = "DepartmentId", TargetKeyColumn = "Id", JoinType = "Left" });
        m.Columns.Add(new ReportColumnModel { TableAlias = "t1", Field = dept.Fields["Name"], OutputCode = "c1" });

        var (sql, _) = ReportSqlBuilder.Build(m, Guid.NewGuid(), 100);
        sql.Should().Contain("LEFT JOIN \"Departments\" t1 ON t1.\"Id\" = t0.\"DepartmentId\"");
        sql.Should().Contain("t1.\"Name\" AS \"c1\"");
    }

    [Fact]
    public void Binds_filter_values_as_parameters()
    {
        var m = BaseModel();
        m.Filters.Add(new ReportFilterModel { TableAlias = "t0", Field = Employee().Fields["Salary"], Operator = ReportFilterOperator.GreaterThan, Value = "5000" });
        var (sql, ps) = ReportSqlBuilder.Build(m, Guid.NewGuid(), 100);
        sql.Should().Contain("t0.\"BasicSalary\" > @p1");
        ps[1].Should().Be(5000);   // converted to the field CLR type
    }

    [Fact]
    public void Orders_by_sort_fields()
    {
        var m = BaseModel();
        m.Sorts.Add(new ReportSortModel { TableAlias = "t0", Field = Employee().Fields["Salary"], Direction = SortDirection.Descending });
        var (sql, _) = ReportSqlBuilder.Build(m, Guid.NewGuid(), 100);
        sql.Should().Contain("ORDER BY t0.\"BasicSalary\" DESC");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ReportSqlBuilderTests`
Expected: FAIL — `ReportSqlBuilder` does not exist.

- [ ] **Step 3: Implement** (reuse the quoting/param conventions from `WidgetDataService`)

```csharp
using System.Globalization;
using System.Text;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Catalog;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure SQL construction for a resolved report plan. Identifiers are already validated
/// upstream (every alias/column comes from a ResolvedObject/ResolvedField); values are bound as
/// parameters. Emits LIMIT rowCap+1 so the caller can detect truncation.</summary>
public static class ReportSqlBuilder
{
    public static (string Sql, IReadOnlyList<object?> Parameters) Build(ReportQueryModel model, Guid tenantId, int rowCap)
    {
        var ps = new List<object?>();
        string P(object? v) { ps.Add(v ?? DBNull.Value); return "@p" + (ps.Count - 1); }

        // SELECT
        var select = string.Join(", ", model.Columns.Select(c => $"{c.TableAlias}.{Q(c.Field.ColumnName)} AS {Q(c.OutputCode)}"));
        if (string.IsNullOrEmpty(select)) select = $"{model.PrimaryAlias}.{Q(model.Primary.KeyColumn)}";

        // FROM + JOINs
        var sb = new StringBuilder();
        sb.Append("SELECT ").Append(select)
          .Append(" FROM ").Append(TableRef(model.Primary)).Append(' ').Append(model.PrimaryAlias);
        foreach (var j in model.Joins)
        {
            var kw = j.JoinType?.ToLowerInvariant() switch { "left" => "LEFT JOIN", "right" => "RIGHT JOIN", _ => "INNER JOIN" };
            sb.Append(' ').Append(kw).Append(' ').Append(TableRef(j.Target)).Append(' ').Append(j.Alias)
              .Append(" ON ").Append(j.Alias).Append('.').Append(Q(j.TargetKeyColumn))
              .Append(" = ").Append(j.SourceAlias).Append('.').Append(Q(j.SourceColumn));
        }

        // WHERE: tenant + soft-delete (primary) then filters
        var where = new List<string>();
        if (model.Primary.HasTenant) where.Add($"{model.PrimaryAlias}.{Q("TenantId")} = {P(tenantId)}");
        if (model.Primary.HasSoftDelete) where.Add($"{model.PrimaryAlias}.{Q("IsDeleted")} = false");
        foreach (var f in model.Filters) AppendFilter(where, f, P);
        if (where.Count > 0) sb.Append(" WHERE ").Append(string.Join(" AND ", where));

        // ORDER BY
        if (model.Sorts.Count > 0)
            sb.Append(" ORDER BY ").Append(string.Join(", ", model.Sorts.Select(s =>
                $"{s.TableAlias}.{Q(s.Field.ColumnName)} {(s.Direction == SortDirection.Descending ? "DESC" : "ASC")}")));

        sb.Append(" LIMIT ").Append(rowCap + 1);
        return (sb.ToString(), ps);
    }

    private static void AppendFilter(List<string> where, ReportFilterModel f, Func<object?, string> P)
    {
        var col = $"{f.TableAlias}.{Q(f.Field.ColumnName)}";
        switch (f.Operator)
        {
            case ReportFilterOperator.IsNull: where.Add($"{col} IS NULL"); break;
            case ReportFilterOperator.IsNotNull: where.Add($"{col} IS NOT NULL"); break;
            case ReportFilterOperator.Contains: where.Add($"{col}::text ILIKE '%' || {P(f.Value ?? "")} || '%'"); break;
            case ReportFilterOperator.StartsWith: where.Add($"{col}::text ILIKE {P((f.Value ?? "") + "%")}"); break;
            case ReportFilterOperator.EndsWith: where.Add($"{col}::text ILIKE {P("%" + (f.Value ?? ""))}"); break;
            case ReportFilterOperator.NotEquals: where.Add($"{col} <> {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.GreaterThan: where.Add($"{col} > {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.LessThan: where.Add($"{col} < {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.GreaterThanOrEqual: where.Add($"{col} >= {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.LessThanOrEqual: where.Add($"{col} <= {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.Between:
                where.Add($"{col} BETWEEN {P(Convert(f.Value, f.Field))} AND {P(Convert(f.ValueTo, f.Field))}"); break;
            case ReportFilterOperator.In:
                var vals = (f.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (vals.Length > 0) where.Add($"{col} IN ({string.Join(",", vals.Select(v => P(Convert(v, f.Field))))})");
                break;
            case ReportFilterOperator.NotIn:
                var nvals = (f.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (nvals.Length > 0) where.Add($"{col} NOT IN ({string.Join(",", nvals.Select(v => P(Convert(v, f.Field))))})");
                break;
            default: where.Add($"{col} = {P(Convert(f.Value, f.Field))}"); break;
        }
    }

    private static object? Convert(string? raw, ResolvedField field)
    {
        if (raw is null) return DBNull.Value;
        var t = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        try
        {
            if (t.IsEnum) return int.TryParse(raw, out var ev) ? ev : (int)Enum.Parse(t, raw, true);
            if (t == typeof(Guid)) return Guid.Parse(raw);
            if (t == typeof(bool)) return raw is "1" or "true" or "True";
            if (t == typeof(int) || t == typeof(short) || t == typeof(byte)) return int.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(double) || t == typeof(float)) return double.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(DateTime))
                return DateTime.SpecifyKind(DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), DateTimeKind.Utc);
            if (t == typeof(DateOnly)) return DateOnly.Parse(raw, CultureInfo.InvariantCulture);
            return raw;
        }
        catch { return raw; }   // upstream validation should prevent this; fall back to raw string
    }

    private static string TableRef(ResolvedObject o) => o.Schema is { Length: > 0 } s ? $"{Q(s)}.{Q(o.TableName)}" : Q(o.TableName);
    private static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ReportSqlBuilderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportSqlBuilder.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportSqlBuilderTests.cs
git commit -m "feat(reports): pure SQL builder with joins, filters, sorting, scoping"
```

---

### Task 5: ReportRowShaper (multi-level grouping + aggregates + computed fields)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportRowShaper.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportRowShaperTests.cs`

**Interfaces:**
- Consumes: `ReportRow`, `ReportResult`, `ReportGroup`, `ReportColumn` (Task 1); `AggregationType`, `SortDirection` (enums).
- Produces:
  - `sealed class ReportRowShaper`
    - ctor `(ComputedFieldEvaluator evaluator)`
    - `ReportResult Shape(IReadOnlyList<ReportRow> rows, ReportShapeSpec spec)`
  - `sealed class ReportShapeSpec { List<ReportColumn> Columns; List<ComputedColumnSpec> Computed; List<string> GroupByCodes; List<(string Code, SortDirection Dir)> InMemorySorts; string ReportCode; int Page; int PageSize; bool Truncated; }`
  - `sealed class ComputedColumnSpec { string Code; HR.Domain.Engines.Finance.Expressions.Expr Ast; }`
- Behavior: evaluate computed columns into each row; compute per-group aggregates (Sum/Avg/Count/Min/Max) and grand totals over measure columns; nest groups in `GroupByCodes` order; when no grouping, page the flat rows.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportRowShaperTests
{
    private readonly ReportRowShaper _shaper = new(new ComputedFieldEvaluator());

    private static ReportRow Row(string dept, decimal salary) =>
        new() { ["Department"] = dept, ["Salary"] = salary };

    private static ReportShapeSpec Spec(bool grouped) => new()
    {
        ReportCode = "TEST",
        Columns = new()
        {
            new ReportColumn { Code = "Department", Label = "Dept", Type = "Text" },
            new ReportColumn { Code = "Salary", Label = "Salary", Type = "Number", IsMeasure = true, Aggregation = AggregationType.Sum },
        },
        GroupByCodes = grouped ? new() { "Department" } : new(),
        Page = 1, PageSize = 50,
    };

    [Fact]
    public void Groups_rows_and_sums_measures()
    {
        var rows = new List<ReportRow> { Row("HR", 100m), Row("HR", 200m), Row("IT", 50m) };
        var result = _shaper.Shape(rows, Spec(grouped: true));

        result.Groups.Should().HaveCount(2);
        var hr = result.Groups.Single(g => (string)g.Key! == "HR");
        hr.Count.Should().Be(2);
        hr.Aggregates["Salary"].Should().Be(300);
        result.GrandTotals["Salary"].Should().Be(350);
    }

    [Fact]
    public void Flat_result_pages_rows_when_no_grouping()
    {
        var rows = Enumerable.Range(0, 120).Select(i => Row("HR", i)).ToList();
        var spec = Spec(grouped: false); spec.PageSize = 50;
        var result = _shaper.Shape(rows, spec);
        result.Groups.Should().BeEmpty();
        result.Rows.Should().HaveCount(50);
        result.TotalCount.Should().Be(120);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ReportRowShaperTests`
Expected: FAIL — `ReportRowShaper` / `ReportShapeSpec` do not exist.

- [ ] **Step 3: Implement**

```csharp
using HR.Domain.Enums;
using HR.Domain.Engines.Finance.Expressions;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ComputedColumnSpec
{
    public string Code { get; set; } = null!;
    public Expr Ast { get; set; } = null!;
}

public sealed class ReportShapeSpec
{
    public string ReportCode { get; set; } = null!;
    public List<ReportColumn> Columns { get; set; } = new();
    public List<ComputedColumnSpec> Computed { get; set; } = new();
    public List<string> GroupByCodes { get; set; } = new();
    public List<(string Code, SortDirection Dir)> InMemorySorts { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool Truncated { get; set; }
}

/// <summary>Pure shaping: evaluates computed columns per row, applies in-memory sorts,
/// builds nested groups with measure aggregates + grand totals. No DB access.</summary>
public sealed class ReportRowShaper
{
    private readonly ComputedFieldEvaluator _evaluator;
    public ReportRowShaper(ComputedFieldEvaluator evaluator) => _evaluator = evaluator;

    public ReportResult Shape(IReadOnlyList<ReportRow> rows, ReportShapeSpec spec)
    {
        var working = rows.Select(r => new ReportRow(r)).ToList();

        // 1. Computed columns
        foreach (var row in working)
            foreach (var c in spec.Computed)
                row[c.Code] = _evaluator.Evaluate(c.Ast, row);

        // 2. In-memory sorts (needed for computed-field sorts; harmless for object fields)
        IEnumerable<ReportRow> sorted = working;
        foreach (var s in Enumerable.Reverse(spec.InMemorySorts))
            sorted = s.Dir == SortDirection.Descending
                ? sorted.OrderByDescending(r => r.GetValueOrDefault(s.Code))
                : sorted.OrderBy(r => r.GetValueOrDefault(s.Code));
        working = sorted.ToList();

        var measures = spec.Columns.Where(c => c.IsMeasure && c.Aggregation is not null).ToList();
        var result = new ReportResult
        {
            ReportCode = spec.ReportCode, Columns = spec.Columns,
            TotalCount = working.Count, Page = spec.Page, PageSize = spec.PageSize, Truncated = spec.Truncated,
            GrandTotals = Aggregate(working, measures),
        };

        if (spec.GroupByCodes.Count == 0)
        {
            result.Rows = working.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToList();
            return result;
        }

        result.Groups = BuildGroups(working, spec.GroupByCodes, 0, measures);
        return result;
    }

    private List<ReportGroup> BuildGroups(List<ReportRow> rows, List<string> groupCodes, int level, List<ReportColumn> measures)
    {
        var code = groupCodes[level];
        var groups = new List<ReportGroup>();
        foreach (var g in rows.GroupBy(r => r.GetValueOrDefault(code)))
        {
            var members = g.ToList();
            var group = new ReportGroup
            {
                FieldCode = code, Key = g.Key, Label = g.Key?.ToString() ?? "—",
                Count = members.Count, Aggregates = Aggregate(members, measures),
            };
            if (level + 1 < groupCodes.Count)
                group.SubGroups = BuildGroups(members, groupCodes, level + 1, measures);
            else
                group.Rows = members;
            groups.Add(group);
        }
        return groups;
    }

    private static Dictionary<string, double> Aggregate(List<ReportRow> rows, List<ReportColumn> measures)
    {
        var totals = new Dictionary<string, double>();
        foreach (var m in measures)
        {
            var nums = rows.Select(r => r.GetValueOrDefault(m.Code)).Where(v => v is not null)
                           .Select(v => System.Convert.ToDouble(v)).ToList();
            totals[m.Code] = m.Aggregation switch
            {
                AggregationType.Sum => nums.Sum(),
                AggregationType.Average => nums.Count > 0 ? nums.Average() : 0,
                AggregationType.Min => nums.Count > 0 ? nums.Min() : 0,
                AggregationType.Max => nums.Count > 0 ? nums.Max() : 0,
                AggregationType.Count => rows.Count,
                _ => nums.Sum(),
            };
        }
        return totals;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter ReportRowShaperTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportRowShaper.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportRowShaperTests.cs
git commit -m "feat(reports): pure row shaper (grouping, aggregates, computed cols)"
```

---

### Task 6: ReportObjectResolver (Guid → catalog bridge + validation)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/IReportObjectResolver.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportObjectResolver.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` (ObjectRegistry `DbSet<ObjectDefinition>`), `IObjectCatalogService.Resolve(code)`, the `ReportDefinition` aggregate (Fields/Filters/Groupings/Sortings/Relationships), `ReportModels` (Task 1).
- Produces:
  - `interface IReportObjectResolver { Task<ReportQueryModel> BuildModelAsync(ReportDefinition report, CancellationToken ct); }`
  - Behavior: load `ObjectDefinition` rows for the primary + related object Guids, map each to its `Code`, `Resolve` each against the catalog (throw `ValidationException` if any is unknown), assign aliases (`t0`, `t1`…), translate `ReportRelationship`→`ReportJoinModel` (validate `JoinField` is a field on the source resolved object), object/relationship `ReportField`→`ReportColumnModel`, `ReportFilter`→`ReportFilterModel` (validate `FieldCode`), `ReportSorting`→`ReportSortModel`. Computed fields (`FieldType == CalculatedField`) are NOT added to `Columns` — they are returned separately (see note) for the shaper.

> Because the resolver must return both the SQL model and the computed-column specs + group codes, have `BuildModelAsync` return a small composite: add `sealed class ReportExecutionModel { ReportQueryModel Query; List<ComputedColumnSpec> Computed; List<string> GroupByCodes; List<(string,SortDirection)> InMemorySorts; List<ReportColumn> OutputColumns; }` in `ReportModels.cs` and have the resolver produce that. Update the interface to `Task<ReportExecutionModel> BuildModelAsync(...)`.

- [ ] **Step 1: Add `ReportExecutionModel` to `ReportModels.cs`**

```csharp
public sealed class ReportExecutionModel
{
    public ReportQueryModel Query { get; set; } = null!;
    public List<ComputedColumnSpec> Computed { get; set; } = new();
    public List<string> GroupByCodes { get; set; } = new();
    public List<(string Code, HR.Domain.Enums.SortDirection Dir)> InMemorySorts { get; set; } = new();
    public List<ReportColumn> OutputColumns { get; set; } = new();
}
```

- [ ] **Step 2: Write the interface**

```csharp
using HR.Domain.Engines.Reports;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportObjectResolver
{
    Task<ReportExecutionModel> BuildModelAsync(ReportDefinition report, CancellationToken ct);
}
```

- [ ] **Step 3: Implement the resolver**

Read `backend/src/HR.Modules/Platform/Services/Catalog/CatalogModels.cs` for the `CatalogFieldDto` shape, and confirm how `ObjectField` maps a report `FieldCode` to a catalog field code (they should match `ResolvedField.Code`). Then:

```csharp
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.ObjectRegistry;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using FluentValidation.Results;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Bridges report definitions (which reference objects by Guid via ObjectRegistry)
/// to the live catalog, validating every table/column/join against IObjectCatalogService.</summary>
public sealed class ReportObjectResolver : IReportObjectResolver
{
    private readonly ApplicationDbContext _db;
    private readonly IObjectCatalogService _catalog;

    public ReportObjectResolver(ApplicationDbContext db, IObjectCatalogService catalog)
    { _db = db; _catalog = catalog; }

    public async Task<ReportExecutionModel> BuildModelAsync(ReportDefinition report, CancellationToken ct)
    {
        // 1. Gather object Guids (primary + relationship targets/sources).
        var objectIds = new HashSet<Guid> { report.PrimaryObjectId };
        foreach (var rel in report.Relationships) { objectIds.Add(rel.SourceObjectId); objectIds.Add(rel.TargetObjectId); }
        var defs = await _db.Set<ObjectDefinition>().AsNoTracking()
            .Where(o => objectIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);

        ResolvedObject ResolveId(Guid id)
        {
            if (!defs.TryGetValue(id, out var def)) throw Invalid("object", $"Unknown object definition '{id}'.");
            return _catalog.Resolve(def.Code) ?? throw Invalid("object", $"Object '{def.Code}' is not discoverable.");
        }

        var primary = ResolveId(report.PrimaryObjectId);
        var query = new ReportQueryModel { Primary = primary, PrimaryAlias = "t0" };
        var aliasByObjectId = new Dictionary<Guid, string> { [report.PrimaryObjectId] = "t0" };

        // 2. Joins (ordered).
        var n = 1;
        foreach (var rel in report.Relationships.OrderBy(r => r.SortOrder))
        {
            var target = ResolveId(rel.TargetObjectId);
            var source = ResolveId(rel.SourceObjectId);
            if (source.Field(rel.JoinField) is null)
                throw Invalid("join", $"Join field '{rel.JoinField}' is not a field of '{source.Code}'.");
            var alias = "t" + n++;
            aliasByObjectId[rel.TargetObjectId] = alias;
            var sourceAlias = aliasByObjectId.GetValueOrDefault(rel.SourceObjectId, "t0");
            query.Joins.Add(new ReportJoinModel
            {
                Alias = alias, Target = target, SourceAlias = sourceAlias,
                SourceColumn = source.Field(rel.JoinField)!.ColumnName,
                TargetKeyColumn = target.KeyColumn, JoinType = rel.JoinType,
            });
        }

        string AliasFor(Guid? objId) => objId is { } id && aliasByObjectId.TryGetValue(id, out var a) ? a : "t0";
        ResolvedObject ObjFor(Guid? objId) => objId is { } id && id != report.PrimaryObjectId && defs.ContainsKey(id) ? ResolveId(id) : primary;

        var model = new ReportExecutionModel { Query = query };

        // 3. Fields → SQL columns / computed specs / output columns.
        var outCode = 0;
        foreach (var f in report.Fields.Where(f => f.IsVisible).OrderBy(f => f.SortOrder))
        {
            var col = new ReportColumn
            {
                Code = f.FieldCode, Label = f.DisplayNameAr, FormatPattern = f.FormatPattern,
                IsMeasure = f.Aggregation is not null, Aggregation = f.Aggregation,
            };
            model.OutputColumns.Add(col);

            if (f.FieldType == ReportFieldType.CalculatedField)
            {
                if (string.IsNullOrWhiteSpace(f.CalculationExpression))
                    throw Invalid("field", $"Computed field '{f.FieldCode}' has no expression.");
                model.Computed.Add(new ComputedColumnSpec { Code = f.FieldCode, Ast = ParseAst(f.CalculationExpression) });
                continue;
            }

            var obj = ObjFor(f.ObjectDefinitionId);
            var rf = obj.Field(f.FieldCode) ?? throw Invalid("field", $"Field '{f.FieldCode}' not found on '{obj.Code}'.");
            col.Type = rf.Kind.ToString();
            query.Columns.Add(new ReportColumnModel { TableAlias = AliasFor(f.ObjectDefinitionId), Field = rf, OutputCode = f.FieldCode });
        }

        // 4. Filters (object-field filters push to SQL; computed-field filters are applied post-shape — R1 supports object filters, see note).
        foreach (var flt in report.Filters)
        {
            var rf = primary.Field(flt.FieldCode);
            if (rf is null) continue; // unknown → ignored, never injected
            query.Filters.Add(new ReportFilterModel
            {
                TableAlias = "t0", Field = rf, Operator = flt.Operator, Value = flt.Value, ValueTo = flt.ValueTo,
            });
        }

        // 5. Sortings: object fields → SQL ORDER BY; computed → in-memory.
        var computedCodes = model.Computed.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in report.Sortings.OrderBy(s => s.SortOrder))
        {
            if (computedCodes.Contains(s.FieldCode)) { model.InMemorySorts.Add((s.FieldCode, s.Direction)); continue; }
            var rf = primary.Field(s.FieldCode);
            if (rf is not null) query.Sorts.Add(new ReportSortModel { TableAlias = "t0", Field = rf, Direction = s.Direction });
        }

        // 6. Group-by codes (order by SortOrder).
        model.GroupByCodes = report.Groupings.OrderBy(g => g.SortOrder).Select(g => g.FieldCode).ToList();

        return model;
    }

    private static Expr ParseAst(string calculationExpression)
        // CalculationExpression stores the AST as JSON (produced by the builder UI). Use the existing
        // AstJson deserializer. Confirm the exact method name in HR.Domain/Engines/Finance/Expressions/AstJson.cs.
        => HR.Domain.Engines.Finance.Expressions.AstJson.Deserialize(calculationExpression);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
```

> **Note (R1 filter scope):** filters on computed/relationship fields are deferred to a follow-up within Phase 1 if needed — the resolver above pushes object-field filters (on the primary) to SQL. Relationship-field filters can be added by resolving `flt` against the joined object using the same `AliasFor`/`ObjFor` pattern; add a test first when you implement that.

- [ ] **Step 4: Build (no unit test — this is DB-bound; covered by the Task 8 integration test)**

Run: `dotnet build backend/src/HR.Modules/HR.Modules.csproj`
Expected: build succeeds. Fix any `AstJson.Deserialize` / catalog API name mismatches against the real files.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportObjectResolver.cs backend/src/HR.Modules/Platform/Services/Reports/ReportObjectResolver.cs backend/src/HR.Modules/Platform/Services/Reports/ReportModels.cs
git commit -m "feat(reports): resolve report Guids to validated catalog metadata"
```

---

### Task 7: ReportExecutionService (orchestration + ADO)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/IReportExecutionService.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportExecutionService.cs`

**Interfaces:**
- Consumes: `IReportObjectResolver` (Task 6), `ReportSqlBuilder` (Task 4), `ReportRowShaper` (Task 5), `ApplicationDbContext`, `ICurrentUserService` (`TenantId`), the ADO pattern from `WidgetDataService` (`GetDbConnection`, `CreateCommand`, `OpenAsync`, `ReadAsync`).
- Produces:
  - `interface IReportExecutionService { Task<ReportResult> RunAsync(Guid reportId, int page, int pageSize, CancellationToken ct); }`
  - Behavior: load `ReportDefinition` with all children (fail `NotFoundException` if missing); build the execution model; build SQL (rowCap from config, default 5000); execute ADO → `List<ReportRow>` keyed by `OutputCode`/field code; detect truncation (rows > rowCap); shape; return.

- [ ] **Step 1: Write the interface**

```csharp
namespace HR.Modules.Platform.Services.Reports;

public interface IReportExecutionService
{
    Task<ReportResult> RunAsync(Guid reportId, int page, int pageSize, CancellationToken ct);
}
```

- [ ] **Step 2: Implement the service** (mirror `WidgetDataService` ADO helpers)

```csharp
using System.Data;
using System.Data.Common;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportExecutionService : IReportExecutionService
{
    private const int RowCap = 5000;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IReportObjectResolver _resolver;

    public ReportExecutionService(ApplicationDbContext db, ICurrentUserService user, IReportObjectResolver resolver)
    { _db = db; _user = user; _resolver = resolver; }

    public async Task<ReportResult> RunAsync(Guid reportId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var report = await _db.Set<ReportDefinition>().AsNoTracking()
            .Include(r => r.Fields).Include(r => r.Filters).Include(r => r.Groupings)
            .Include(r => r.Sortings).Include(r => r.Relationships)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("ReportDefinition", reportId);

        var model = await _resolver.BuildModelAsync(report, ct);
        var (sql, ps) = ReportSqlBuilder.Build(model.Query, _user.TenantId, RowCap);

        var rows = new List<ReportRow>();
        await ReadAsync(sql, ps, ct, reader =>
        {
            var row = new ReportRow();
            foreach (var c in model.Query.Columns)
            {
                var ord = reader.GetOrdinal(c.OutputCode);
                row[c.OutputCode] = reader.IsDBNull(ord) ? null : reader.GetValue(ord);
            }
            rows.Add(row);
        });

        var truncated = rows.Count > RowCap;
        if (truncated) rows = rows.Take(RowCap).ToList();

        var shaper = new ReportRowShaper(new ComputedFieldEvaluator());
        return shaper.Shape(rows, new ReportShapeSpec
        {
            ReportCode = report.Code, Columns = model.OutputColumns, Computed = model.Computed,
            GroupByCodes = model.GroupByCodes, InMemorySorts = model.InMemorySorts,
            Page = page, PageSize = pageSize, Truncated = truncated,
        });
    }

    // ── ADO (mirrors WidgetDataService) ──
    private async Task ReadAsync(string sql, IReadOnlyList<object?> ps, CancellationToken ct, Action<DbDataReader> onRow)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < ps.Count; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = "p" + i;
            param.Value = ps[i] ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) onRow(reader);
        }
        finally { if (opened) await conn.CloseAsync(); }
    }
}
```

> The reader keys rows by `OutputCode`. In Task 6 the SQL `OutputCode` was set to `f.FieldCode`, so shaper column codes and row keys align. Keep these identical.

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/HR.Modules/HR.Modules.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportExecutionService.cs backend/src/HR.Modules/Platform/Services/Reports/ReportExecutionService.cs
git commit -m "feat(reports): execution service orchestrating resolve/build/run/shape"
```

---

### Task 8: Wire the `run` endpoint + DI

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs` (add `RunReportQuery` + handler)
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs` (add `POST {id}/run`)
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` (register services)

**Interfaces:**
- Consumes: `IReportExecutionService` (Task 7), existing `BaseApiController.OkResponse`, `RequirePermission`, `ApiResponse<T>`.
- Produces: `record RunReportQuery(Guid Id, int Page, int PageSize) : IRequest<ReportResult>;` and `POST /api/platform/reports/{id}/run`.

- [ ] **Step 1: Add the query + handler to `ReportQueries.cs`**

```csharp
// add usings: using HR.Modules.Platform.Services.Reports;
public record RunReportQuery(Guid Id, int Page, int PageSize) : IRequest<ReportResult>;

public class RunReportQueryHandler : IRequestHandler<RunReportQuery, ReportResult>
{
    private readonly IReportExecutionService _exec;
    public RunReportQueryHandler(IReportExecutionService exec) => _exec = exec;
    public Task<ReportResult> Handle(RunReportQuery request, CancellationToken ct)
        => _exec.RunAsync(request.Id, request.Page, request.PageSize, ct);
}
```

- [ ] **Step 2: Add the endpoint to `ReportsController.cs`**

```csharp
[HttpPost("{id:guid}/run")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<ReportResult>>> Run(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
{ var result = await Mediator.Send(new RunReportQuery(id, page, pageSize), ct); return OkResponse(result); }
```

Add `using HR.Modules.Platform.Services.Reports;` to the controller.

- [ ] **Step 3: Register services in DI**

Read `DependencyInjection.cs` to match its registration style (it already registers `IWidgetDataService`/`IObjectCatalogService`). Add:

```csharp
services.AddScoped<IReportObjectResolver, ReportObjectResolver>();
services.AddScoped<IReportExecutionService, ReportExecutionService>();
```

- [ ] **Step 4: Build the whole backend**

Run: `dotnet build backend/HR.sln`
Expected: build succeeds.

- [ ] **Step 5: Integration test — run a real report end to end**

**Files:** Create `backend/tests/HR.Modules.Platform.Tests/Reports/ReportExecutionIntegrationTests.cs`.

This needs a Postgres database. The Azure dev DB is available (see `CLAUDE.md`), or use a local Postgres. Guard the test so it skips when no connection string is configured:

```csharp
using FluentAssertions;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportExecutionIntegrationTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    [SkippableFact]
    public async Task Runs_employee_by_department_report_with_sum()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run this integration test.");
        // Arrange: build an ApplicationDbContext on Conn, seed 1 tenant + 1 department + 2 employees,
        // a ReportDefinition (primary=Employee) with:
        //   - a Department join (ReportRelationship on Employee.DepartmentId),
        //   - fields: Department.Name, Employee.BasicSalary (Aggregation=Sum),
        //   - grouping: Department.Name.
        // Construct ReportExecutionService with a stub ICurrentUserService returning the seeded TenantId.
        // Act: RunAsync(reportId, 1, 50, ct).
        // Assert: one group per department, group.Aggregates["BasicSalary"] equals the seeded sum.
        // (Implement the seeding using the same EF context the app uses; keep it in a transaction and roll back.)
        true.Should().BeTrue(); // replace with the real arrange/act/assert above
    }
}
```

> Requires the `Xunit.SkippableFact` package (add to the test csproj). Flesh out the seeding using `ApplicationDbContext` against `REPORTS_TEST_DB`. If setting up a seeded Postgres is out of scope for this pass, mark the test `[Fact(Skip = "...")]` and cover execution via manual Swagger verification in Step 6 — but prefer the automated test.

- [ ] **Step 6: Manual verification via Swagger**

Run the API locally (`dotnet run --project backend/src/HR.Api`), open `/swagger`, create a small report definition (POST `/api/platform/reports` + add fields/groupings), then `POST /api/platform/reports/{id}/run`. Confirm a grouped result with aggregates returns.

- [ ] **Step 7: Commit**

```bash
git add backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs backend/src/HR.Modules/Platform/Controllers/ReportsController.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportExecutionIntegrationTests.cs
git commit -m "feat(reports): run endpoint + DI wiring + integration test"
```

---

## Phase 1 Self-Review (completed by plan author)

- **Spec coverage:** execution engine (Tasks 4–7), joins (Task 6), computed fields (Tasks 3, 6), multi-level grouping + aggregates (Task 5), dynamic sort/filter (Tasks 4, 6), access resolver (Task 2), run endpoint (Task 8), injection gate (Tasks 4, 6 validation) — all covered. Ownership/sharing *management endpoints*, organization (folders/tags/favorites), Excel/CSV export, and the frontend are **Phase 2/3** (below), intentionally out of this plan.
- **Type consistency:** `OutputCode` = `FieldCode` links SQL columns (Task 6) ↔ reader keys (Task 7) ↔ shaper column codes (Task 5). `ReportColumn.Aggregation` is `AggregationType?` throughout. `ComputedColumnSpec.Ast` is `Expr` in Tasks 5 and 6.
- **Known API-alignment risks (flagged in-task):** exact `RuleValue`/`FunctionRegistry`/`AstJson` member names (Tasks 3, 6) must be confirmed against the real Finance expression engine before implementing — the plan says so at each site.

---

## Later Phases (separate plans, written after Phase 1 lands)

- **Phase 2 — Sharing & Organization (backend):** migration `ReportOrganization` (`ReportFolder`, `ReportTag`, `ReportDefinitionTag`, `ReportUserState`; `ReportDefinition.FolderId`); share-management endpoints; access resolver wired into list/get/run; folders/tags/favorites/pin/recent endpoints; `Platform.Reports.*` permission seeding.
- **Phase 3 — Frontend + Excel/CSV export:** reports list (folders/favorites/recent/tags), builder wizard (object → joins → fields incl. computed formula → filters → grouping/sorting → save/share), viewer (grouped table + subtotals + params), and `GET {id}/export?format=excel|csv` via the existing `IExportWriter`.

(R2 PDF+streaming, R3 SIF/WPS, R4 scheduled delivery follow as their own specs/plans.)
