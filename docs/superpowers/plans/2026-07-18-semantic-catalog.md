# Semantic Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a read-only Semantic Catalog — a code-defined presentation layer over the existing object catalog that exposes business Domains, friendly Objects/Fields, and first-class Metrics — behind a stable API + `ISemanticCatalogProvider` abstraction, with zero engine changes.

**Architecture:** Contract DTOs + `ISemanticCatalogProvider` live in `HR.Application` (consumers depend on the abstraction). The implementation (`CodeDefinedSemanticCatalog` + a curated `CatalogRegistry` + a `MetricSpecMapper` that translates a metric to the existing `WidgetQuerySpec`) lives in `HR.Modules.Platform` and validates everything against the existing `IObjectCatalogService`, self-hiding anything that doesn't resolve. A thin `SemanticCatalogController` exposes it.

**Tech Stack:** .NET 8, xUnit 2.9.2 + FluentAssertions 6.12.1. No new packages. No DB, no migration.

## Global Constraints

- **No engine changes.** Do not modify `IObjectCatalogService`, `WidgetDataService`, `WidgetQuerySpec`, Report engine, or any execution code. The catalog only *reads* `IObjectCatalogService`.
- **No `WidgetQuerySpec` in the public contract.** It is an internal mapping target only (used by `MetricSpecMapper`, in `HR.Modules.Platform`). The `HR.Application` contract never references it.
- **No CLR/entity/column names in displayed fields.** `ObjectCode`/`FieldCode` are opaque stable tokens passed back by the UI, never shown. All human-facing text is `NameAr`/`NameEn`/`Description*`.
- **Codes are stable + immutable.** Domain/object/field/metric/group codes are lowercase snake or PascalCase entity tokens; never rename an existing code.
- **Read-only, no tenant overrides** this phase. Provider registered **scoped** (depends on scoped `IObjectCatalogService`).
- Layering: `HR.Application` must NOT reference `HR.Modules.Platform`. The mapper and provider are in Platform; the interface + DTOs are in Application.
- Self-hidden items must be observable: recorded in `CatalogHealth.Hidden` with a reason and logged.
- Permission strings are plain `"Module.Action"` (e.g. `"Employees.View"`, `"Payroll.View"`, `"Attendance.View"`, `"Leaves.View"`, `"Requests.View"`). Caller permissions come from `ICurrentUserService.Permissions` (`IReadOnlyList<string>`).

---

## File map

**Create — `HR.Application/SemanticCatalog/`**
- `Contracts/SemanticContracts.cs` — all DTO records + `SemanticFieldRole` enum.
- `ISemanticCatalogProvider.cs` — interface + `CatalogQueryContext` + `SemanticSearchHit`.

**Create — `HR.Modules/Platform/Services/SemanticCatalog/`**
- `ArabicText.cs` — pure normalizer.
- `RelativeDate.cs` — pure relative-date resolver.
- `MetricSpecMapper.cs` — `SemanticMetricDefinition` → `WidgetQuerySpec` (internal).
- `CatalogRegistry.cs` — the curated data (domains, field groups, objects, fields, metrics, synonyms).
- `CodeDefinedSemanticCatalog.cs` — the provider impl.

**Create — `HR.Modules/Platform/Controllers/`**
- `SemanticCatalogController.cs`.

**Create — tests `HR.Modules.Platform.Tests/SemanticCatalog/`**
- `ArabicTextTests.cs`, `RelativeDateTests.cs`, `MetricSpecMapperTests.cs`, `CatalogRegistryTests.cs`, `CodeDefinedSemanticCatalogTests.cs`.

**Modify**
- `HR.Modules/Platform/DependencyInjection.cs` — register `ISemanticCatalogProvider` → `CodeDefinedSemanticCatalog` (scoped).

---

## Task 1: Contract DTOs + provider interface (HR.Application)

**Files:**
- Create: `backend/src/HR.Application/SemanticCatalog/Contracts/SemanticContracts.cs`
- Create: `backend/src/HR.Application/SemanticCatalog/ISemanticCatalogProvider.cs`

**Interfaces:**
- Produces: all records below + `ISemanticCatalogProvider`, `CatalogQueryContext(IReadOnlyCollection<string> Permissions)`, `SemanticSearchHit`.

- [ ] **Step 1: Create `SemanticContracts.cs`** (pure declarations — no test; the compiler is the check)

```csharp
namespace HR.Application.SemanticCatalog.Contracts;

public sealed record SemanticDomain(
    string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, int SortOrder);

public sealed record SemanticFieldGroup(string Code, string NameAr, string NameEn, int SortOrder);

public enum SemanticFieldRole { Dimension, Measure, Filter, Identifier }

public sealed record SemanticField(
    string ObjectCode, string FieldCode,
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string GroupCode, string? Icon, IReadOnlyList<string> Keywords,
    SemanticFieldRole Role, bool DefaultVisible);

public sealed record SemanticSort(string FieldCode, string Direction); // "Ascending"|"Descending"

public sealed record SemanticFilter(
    string FieldCode, string NameAr, string NameEn,
    string ControlType, string? ReferenceObjectCode); // control: select|date-range|search|reference

public sealed record SemanticObject(
    string ObjectCode, string DomainCode,
    string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, IReadOnlyList<string> Keywords, bool DefaultVisible,
    IReadOnlyList<SemanticFieldGroup> FieldGroups,
    SemanticSort? DefaultSort,
    IReadOnlyList<SemanticFilter> DefaultFilters,
    IReadOnlyList<string> RecommendedMetricCodes,
    IReadOnlyList<string> RecommendedReportCodes,
    IReadOnlyList<string> RecommendedWidgetCodes,
    IReadOnlyList<SemanticField> Fields);

public sealed record SemanticMetricFilter(
    string FieldCode, string Operator,
    string? Value = null, string? RelativeValue = null,
    string? ValueTo = null, string? RelativeValueTo = null);

public sealed record SemanticMetricMeasure(
    string Name, string Aggregation, string? AggregationField,
    IReadOnlyList<SemanticMetricFilter> Filters);

public sealed record SemanticMetricDefinition(
    string ObjectCode, string Aggregation, string? AggregationField,
    IReadOnlyList<SemanticMetricFilter> Filters, string? GroupByField,
    string? Formula = null, IReadOnlyList<SemanticMetricMeasure>? Measures = null);

public sealed record SemanticMetric(
    string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn,
    string Icon, string DomainCode, IReadOnlyList<string> RequiredPermissions,
    SemanticMetricDefinition Definition, string DefaultVisualization,
    IReadOnlyList<string> SuggestedFilterFields);

public sealed record HiddenItem(string Kind, string Code, string Reason); // Kind: Object|Field|Metric
public sealed record CatalogHealth(
    int VisibleObjects, int HiddenObjects, int VisibleMetrics, int HiddenMetrics,
    IReadOnlyList<HiddenItem> Hidden);
```

- [ ] **Step 2: Create `ISemanticCatalogProvider.cs`**

```csharp
using HR.Application.SemanticCatalog.Contracts;

namespace HR.Application.SemanticCatalog;

public sealed record CatalogQueryContext(IReadOnlyCollection<string> Permissions);

public sealed record SemanticSearchHit(string Kind, string Code, string NameAr, string NameEn, double Score);

public interface ISemanticCatalogProvider
{
    IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext ctx);
    IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext ctx, string? domainCode = null);
    SemanticObject? GetObject(CatalogQueryContext ctx, string objectCode);
    IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext ctx, string? domainCode = null);
    SemanticMetric? GetMetric(CatalogQueryContext ctx, string metricCode);
    IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext ctx, string query);
    CatalogHealth GetHealth();
}
```

- [ ] **Step 3: Build** `dotnet build backend/src/HR.Application/HR.Application.csproj -v q` → Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Application/SemanticCatalog/
git commit -m "feat(catalog): Semantic Catalog contract DTOs + ISemanticCatalogProvider"
```

---

## Task 2: ArabicText.Normalize (pure, TDD)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/SemanticCatalog/ArabicText.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/ArabicTextTests.cs`

**Interfaces:**
- Produces: `static string ArabicText.Normalize(string input)`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class ArabicTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]     // alef hamza above → bare alef
    [InlineData("إجازة", "اجازه")]   // alef hamza below + taa marbuta → ه
    [InlineData("آمنة", "امنه")]     // alef madda → alef; taa marbuta
    [InlineData("مُوَظَّف", "موظف")]  // strip tashkeel
    [InlineData("رِيـــال", "ريال")]  // strip tatweel + tashkeel
    [InlineData("مصطفى", "مصطفي")]   // alef maqsura → ya
    [InlineData("Payroll", "payroll")] // latin lowercased, untouched otherwise
    public void Normalize_unifies_forms(string input, string expected)
        => ArabicText.Normalize(input).Should().Be(expected);

    [Fact]
    public void Normalize_null_or_empty_is_empty()
    {
        ArabicText.Normalize("").Should().Be("");
        ArabicText.Normalize(null!).Should().Be("");
    }
}
```

- [ ] **Step 2: Run to verify FAIL**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ArabicTextTests`
Expected: FAIL — `ArabicText` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Text;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>Pure Arabic text normalization for search matching: unify alef/taa-marbuta/alef-maqsura,
/// strip tashkeel (diacritics) + tatweel, and lowercase Latin. Not for display.</summary>
public static class ArabicText
{
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case 'أ': case 'إ': case 'آ': case 'ٱ': sb.Append('ا'); break;
                case 'ة': sb.Append('ه'); break;
                case 'ى': sb.Append('ي'); break;
                case 'ـ': break;                          // tatweel
                case >= 'ً' and <= 'ْ': break;  // tashkeel (fathatan..sukun)
                case 'ٰ': break;                     // superscript alef
                default: sb.Append(char.ToLowerInvariant(ch)); break;
            }
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run to verify PASS** (same filter). Expected: PASS (8 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/SemanticCatalog/ArabicText.cs backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/ArabicTextTests.cs
git commit -m "feat(catalog): pure ArabicText.Normalize for search"
```

---

## Task 3: RelativeDate.Resolve (pure, TDD)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/SemanticCatalog/RelativeDate.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/RelativeDateTests.cs`

**Interfaces:**
- Produces: `static DateTime RelativeDate.Resolve(string token, DateTime nowUtc)` returning a UTC date (time = 00:00:00). Tokens: `today`, `today+Nd`, `today-Nd`, `startOfMonth`, `endOfMonth`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using FluentAssertions;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class RelativeDateTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 13, 45, 0, DateTimeKind.Utc);

    [Fact] public void Today_is_date_floor()
        => RelativeDate.Resolve("today", Now).Should().Be(new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void Plus_days()
        => RelativeDate.Resolve("today+30d", Now).Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void Minus_days()
        => RelativeDate.Resolve("today-7d", Now).Should().Be(new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void Start_of_month()
        => RelativeDate.Resolve("startOfMonth", Now).Should().Be(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void End_of_month()
        => RelativeDate.Resolve("endOfMonth", Now).Should().Be(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));

    [Fact] public void Unknown_token_throws()
        => FluentActions.Invoking(() => RelativeDate.Resolve("nonsense", Now)).Should().Throw<FormatException>();
}
```

- [ ] **Step 2: Run to verify FAIL** (filter `~RelativeDateTests`). Expected: FAIL — not defined.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>Pure resolver for relative-date tokens used in metric filters. Returns a UTC date floor.</summary>
public static partial class RelativeDate
{
    public static DateTime Resolve(string token, DateTime nowUtc)
    {
        var today = nowUtc.Date;
        switch (token)
        {
            case "today": return today;
            case "startOfMonth": return new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            case "endOfMonth":
                return new DateTime(today.Year, today.Month,
                    DateTime.DaysInMonth(today.Year, today.Month), 0, 0, 0, DateTimeKind.Utc);
        }
        var m = OffsetRegex().Match(token);
        if (m.Success)
        {
            var days = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return m.Groups[1].Value == "+" ? today.AddDays(days) : today.AddDays(-days);
        }
        throw new FormatException($"Unknown relative-date token '{token}'.");
    }

    [GeneratedRegex(@"^today([+-])(\d+)d$")]
    private static partial Regex OffsetRegex();
}
```

- [ ] **Step 4: Run to verify PASS**. Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/SemanticCatalog/RelativeDate.cs backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/RelativeDateTests.cs
git commit -m "feat(catalog): pure RelativeDate.Resolve for metric filters"
```

---

## Task 4: MetricSpecMapper (TDD)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/SemanticCatalog/MetricSpecMapper.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/MetricSpecMapperTests.cs`

**Interfaces:**
- Consumes: `SemanticMetricDefinition`, `SemanticMetricFilter`, `SemanticMetricMeasure` (Task 1); `RelativeDate` (Task 3); `WidgetQuerySpec`/`WidgetFilterSpec`/`WidgetMeasureSpec` (existing, `HR.Modules.Platform.Services.WidgetData`).
- Produces: `static WidgetQuerySpec MetricSpecMapper.ToWidgetSpec(SemanticMetricDefinition def, DateTime nowUtc)`.

> **Before writing:** open `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataModels.cs` and confirm how `WidgetQuerySpec`, `WidgetFilterSpec`, `WidgetMeasureSpec` are constructed (object-initializer vs positional). The code below uses object initializers; if they are positional records, adapt to positional construction. Confirmed property names: `WidgetQuerySpec { ObjectCode, Aggregation, AggregationField, GroupByField, Visualization, Filters, Formula, Measures }`, `WidgetFilterSpec { Field, Operator, Value }`, `WidgetMeasureSpec { Name, Aggregation, AggregationField, Filters }`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class MetricSpecMapperTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    private static IReadOnlyList<SemanticMetricFilter> NoFilters => Array.Empty<SemanticMetricFilter>();

    [Fact]
    public void Simple_count_passes_through()
    {
        var def = new SemanticMetricDefinition("Employee", "Count", null, NoFilters, null);
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.ObjectCode.Should().Be("Employee");
        spec.Aggregation.Should().Be("Count");
        spec.GroupByField.Should().BeNull();
        spec.Filters.Should().BeEmpty();
    }

    [Fact]
    public void Enum_equals_filter_translates_operator()
    {
        var def = new SemanticMetricDefinition("AttendanceRecord", "Count", null,
            new[] { new SemanticMetricFilter("Status", "Equals", Value: "6") }, null);
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.Filters.Should().ContainSingle();
        spec.Filters[0].Field.Should().Be("Status");
        spec.Filters[0].Operator.Should().Be("eq");
        spec.Filters[0].Value.Should().Be("6");
    }

    [Fact]
    public void Relative_date_filter_resolves_to_literal()
    {
        var def = new SemanticMetricDefinition("Employee", "Count", null,
            new[] { new SemanticMetricFilter("HireDate", "GreaterThanOrEqual", RelativeValue: "startOfMonth") }, null);
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.Filters[0].Operator.Should().Be("gte");
        spec.Filters[0].Value.Should().Be("2026-07-01");
    }

    [Fact]
    public void Formula_metric_maps_measures()
    {
        var def = new SemanticMetricDefinition("LeaveBalance", "Formula", null, NoFilters, null,
            Formula: "m1 + m2 - m3",
            Measures: new[]
            {
                new SemanticMetricMeasure("m1", "Sum", "EntitledDays", NoFilters),
                new SemanticMetricMeasure("m2", "Sum", "CarriedForwardDays", NoFilters),
                new SemanticMetricMeasure("m3", "Sum", "UsedDays", NoFilters),
            });
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.Aggregation.Should().Be("Formula");
        spec.Formula.Should().Be("m1 + m2 - m3");
        spec.Measures.Should().HaveCount(3);
        spec.Measures![0].Name.Should().Be("m1");
        spec.Measures[0].AggregationField.Should().Be("EntitledDays");
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (filter `~MetricSpecMapperTests`). Expected: FAIL — not defined.

- [ ] **Step 3: Implement**

```csharp
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.WidgetData;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>Translates a validated SemanticMetricDefinition into the existing WidgetQuerySpec.
/// This is the ONLY place that knows about WidgetQuerySpec — it never leaks into the public contract.</summary>
internal static class MetricSpecMapper
{
    private static readonly Dictionary<string, string> Ops = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Equals"] = "eq", ["NotEquals"] = "ne",
        ["GreaterThan"] = "gt", ["GreaterThanOrEqual"] = "gte",
        ["LessThan"] = "lt", ["LessThanOrEqual"] = "lte",
        ["Between"] = "between", ["In"] = "in", ["Contains"] = "contains",
    };

    public static WidgetQuerySpec ToWidgetSpec(SemanticMetricDefinition def, DateTime nowUtc) => new()
    {
        ObjectCode = def.ObjectCode,
        Aggregation = def.Aggregation,
        AggregationField = def.AggregationField,
        GroupByField = def.GroupByField,
        Formula = def.Formula,
        Filters = def.Filters.Select(f => MapFilter(f, nowUtc)).ToList(),
        Measures = def.Measures?.Select(m => new WidgetMeasureSpec
        {
            Name = m.Name, Aggregation = m.Aggregation, AggregationField = m.AggregationField,
            Filters = m.Filters.Select(f => MapFilter(f, nowUtc)).ToList(),
        }).ToList(),
    };

    private static WidgetFilterSpec MapFilter(SemanticMetricFilter f, DateTime nowUtc) => new()
    {
        Field = f.FieldCode,
        Operator = Ops.TryGetValue(f.Operator, out var op) ? op : f.Operator,
        Value = ResolveValue(f.Value, f.RelativeValue, nowUtc),
    };

    private static string? ResolveValue(string? literal, string? relative, DateTime nowUtc)
        => relative is not null ? RelativeDate.Resolve(relative, nowUtc).ToString("yyyy-MM-dd") : literal;
}
```

> If `WidgetMeasureSpec` / `WidgetFilterSpec` / `WidgetQuerySpec` are positional records (not init-settable), rewrite the `new() { ... }` blocks as positional constructor calls with the same values.

- [ ] **Step 4: Run to verify PASS**. Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/SemanticCatalog/MetricSpecMapper.cs backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/MetricSpecMapperTests.cs
git commit -m "feat(catalog): MetricSpecMapper (definition -> WidgetQuerySpec)"
```

---

## Task 5: CatalogRegistry — curated data + integrity tests

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/SemanticCatalog/CatalogRegistry.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/CatalogRegistryTests.cs`

**Interfaces:**
- Consumes: all contract records (Task 1).
- Produces: `static class CatalogRegistry` with:
  - `IReadOnlyList<SemanticDomain> Domains`
  - `IReadOnlyList<SemanticFieldGroup> FieldGroups`
  - `IReadOnlyList<SemanticObject> Objects`
  - `IReadOnlyList<SemanticMetric> Metrics`
  - `IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms` (normalized token → expansion tokens)

- [ ] **Step 1: Write the integrity test FIRST**

```csharp
using System.Linq;
using FluentAssertions;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class CatalogRegistryTests
{
    private static readonly string[] KnownViz = { "KpiCard", "BarChart", "LineChart", "PieChart", "Table", "Gauge" };
    private static readonly string[] KnownAgg = { "Count", "Sum", "Average", "Min", "Max", "DistinctCount", "Formula" };

    [Fact] public void Domain_codes_unique()
        => CatalogRegistry.Domains.Select(d => d.Code).Should().OnlyHaveUniqueItems();

    [Fact] public void Object_codes_unique()
        => CatalogRegistry.Objects.Select(o => o.ObjectCode).Should().OnlyHaveUniqueItems();

    [Fact] public void Metric_codes_unique()
        => CatalogRegistry.Metrics.Select(m => m.Code).Should().OnlyHaveUniqueItems();

    [Fact] public void Field_group_codes_unique()
        => CatalogRegistry.FieldGroups.Select(g => g.Code).Should().OnlyHaveUniqueItems();

    [Fact]
    public void Every_object_domain_is_defined()
    {
        var domains = CatalogRegistry.Domains.Select(d => d.Code).ToHashSet();
        CatalogRegistry.Objects.Select(o => o.DomainCode).Should().OnlyContain(d => domains.Contains(d));
    }

    [Fact]
    public void Every_field_group_is_defined_globally()
    {
        var groups = CatalogRegistry.FieldGroups.Select(g => g.Code).ToHashSet();
        foreach (var o in CatalogRegistry.Objects)
            o.Fields.Select(f => f.GroupCode).Should().OnlyContain(g => groups.Contains(g), $"object {o.ObjectCode}");
    }

    [Fact]
    public void Every_metric_is_well_formed()
    {
        var domains = CatalogRegistry.Domains.Select(d => d.Code).ToHashSet();
        foreach (var m in CatalogRegistry.Metrics)
        {
            domains.Should().Contain(m.DomainCode, $"metric {m.Code} domain");
            m.RequiredPermissions.Should().NotBeEmpty($"metric {m.Code} permissions");
            KnownViz.Should().Contain(m.DefaultVisualization, $"metric {m.Code} viz");
            KnownAgg.Should().Contain(m.Definition.Aggregation, $"metric {m.Code} agg");
            if (m.Definition.Aggregation == "Formula")
            {
                m.Definition.Formula.Should().NotBeNullOrWhiteSpace($"metric {m.Code} formula");
                m.Definition.Measures.Should().NotBeNullOrEmpty($"metric {m.Code} measures");
            }
        }
    }

    [Fact]
    public void Has_the_seventeen_named_metrics()
    {
        var expected = new[]
        {
            "total_employees","active_employees","new_employees","employees_by_department",
            "gross_payroll","net_payroll","total_deductions","late_employees","absent_employees",
            "overtime_minutes","remaining_leave_balance","pending_requests","expiring_contracts",
            "expiring_documents","total_gosi","total_additions","pending_approvals",
        };
        CatalogRegistry.Metrics.Select(m => m.Code).Should().Contain(expected);
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (filter `~CatalogRegistryTests`). Expected: FAIL — `CatalogRegistry` not defined.

- [ ] **Step 3: Implement `CatalogRegistry.cs`.** Author the curated data. Use the exact field bindings from the spec's confirmed-facts note. Structure (abbreviated skeleton — the implementer fills every domain/object/metric per the spec's tables; all 9 domains, the 7 field groups, the ~8 objects with their fields, and all 17 metrics):

```csharp
using HR.Application.SemanticCatalog.Contracts;
using static HR.Application.SemanticCatalog.Contracts.SemanticFieldRole;

namespace HR.Modules.Platform.Services.SemanticCatalog;

public static class CatalogRegistry
{
    public static readonly IReadOnlyList<SemanticDomain> Domains = new[]
    {
        new SemanticDomain("employees",  "الموظفون", "Employees",  "بيانات الموظفين", "Employee data", "Users", 1),
        new SemanticDomain("payroll",    "الرواتب",  "Payroll",    "الرواتب والاستحقاقات", "Payroll & earnings", "Wallet", 2),
        new SemanticDomain("attendance", "الحضور",   "Attendance", "الحضور والانصراف", "Attendance", "Clock", 3),
        new SemanticDomain("leaves",     "الإجازات", "Leaves",     "أرصدة وطلبات الإجازات", "Leave balances & requests", "CalendarDays", 4),
        new SemanticDomain("requests",   "الطلبات",  "Requests",   "طلبات الموظفين", "Employee requests", "Inbox", 5),
        new SemanticDomain("loans",      "السلف",    "Loans",      "سلف الموظفين", "Employee loans", "HandCoins", 6),
        new SemanticDomain("expenses",   "المصروفات","Expenses",   "مطالبات المصروفات", "Expense claims", "Receipt", 7),
        new SemanticDomain("documents",  "المستندات","Documents",  "مستندات الموظفين", "Employee documents", "FolderOpen", 8),
        new SemanticDomain("recruitment","التوظيف",  "Recruitment","التوظيف والتعيين", "Hiring & recruitment", "UserPlus", 9),
    };

    public static readonly IReadOnlyList<SemanticFieldGroup> FieldGroups = new[]
    {
        new SemanticFieldGroup("personal_information", "المعلومات الشخصية", "Personal Information", 1),
        new SemanticFieldGroup("employment",  "التوظيف",   "Employment",   2),
        new SemanticFieldGroup("organization","الهيكل التنظيمي", "Organization", 3),
        new SemanticFieldGroup("payroll",     "الرواتب",   "Payroll",      4),
        new SemanticFieldGroup("attendance",  "الحضور",    "Attendance",   5),
        new SemanticFieldGroup("leave",       "الإجازات",  "Leave",        6),
        new SemanticFieldGroup("documents",   "المستندات", "Documents",    7),
    };

    // Objects: one SemanticObject per live domain primary entity. Each lists its fields assigned to a
    // field group with Ar/En names + Role. Employee example (author the rest — PayrollPayslip,
    // AttendanceRecord, LeaveBalance, RequestInstance, Loan, the expense entity, EmployeeDocument):
    public static readonly IReadOnlyList<SemanticObject> Objects = new[]
    {
        new SemanticObject(
            ObjectCode: "Employee", DomainCode: "employees",
            NameAr: "الموظفون", NameEn: "Employees",
            DescriptionAr: "سجل الموظفين", DescriptionEn: "Employee records",
            Icon: "Users", Keywords: new[] { "employee","staff","موظف","موظفين" }, DefaultVisible: true,
            FieldGroups: new[]
            {
                new SemanticFieldGroup("personal_information", "المعلومات الشخصية", "Personal Information", 1),
                new SemanticFieldGroup("employment", "التوظيف", "Employment", 2),
                new SemanticFieldGroup("organization", "الهيكل التنظيمي", "Organization", 3),
            },
            DefaultSort: new SemanticSort("HireDate", "Descending"),
            DefaultFilters: new[]
            {
                new SemanticFilter("DepartmentId", "الإدارة", "Department", "reference", "Department"),
                new SemanticFilter("BranchId", "الفرع", "Branch", "reference", "Branch"),
            },
            RecommendedMetricCodes: new[] { "total_employees","active_employees","new_employees","employees_by_department","expiring_contracts" },
            RecommendedReportCodes: Array.Empty<string>(),
            RecommendedWidgetCodes: Array.Empty<string>(),
            Fields: new[]
            {
                new SemanticField("Employee","FirstNameAr","الاسم الأول","First Name","","","personal_information",null,new[]{"name","اسم"},Dimension,true),
                new SemanticField("Employee","Status","الحالة","Status","حالة الموظف","Employment status","employment",null,new[]{"status","حالة"},Dimension,true),
                new SemanticField("Employee","HireDate","تاريخ التعيين","Hire Date","","","employment",null,new[]{"hire","تعيين"},Dimension,true),
                new SemanticField("Employee","DepartmentId","الإدارة","Department","","","organization",null,new[]{"department","ادارة"},Dimension,true),
                new SemanticField("Employee","BranchId","الفرع","Branch","","","organization",null,new[]{"branch","فرع"},Dimension,true),
                new SemanticField("Employee","JobTitleId","المسمى الوظيفي","Job Title","","","employment",null,new[]{"job","title","وظيفة"},Dimension,true),
                new SemanticField("Employee","ContractEndDate","نهاية العقد","Contract End","","","employment",null,new[]{"contract","عقد"},Dimension,true),
                new SemanticField("Employee","BasicSalary","الراتب الأساسي","Basic Salary","","","payroll",null,new[]{"salary","راتب"},Measure,true),
            }),
        // … PayrollPayslip, AttendanceRecord, LeaveBalance, RequestInstance, Loan, Expense, EmployeeDocument …
    };

    // Metrics: all 17 from the spec table. Examples of each shape:
    public static readonly IReadOnlyList<SemanticMetric> Metrics = new[]
    {
        new SemanticMetric("total_employees","إجمالي الموظفين","Total Employees",
            "عدد جميع الموظفين","Count of all employees","Users","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count",null,Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("active_employees","الموظفون النشطون","Active Employees",
            "الموظفون بحالة نشط","Employees with Active status","UserCheck","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"1") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("new_employees","التعيينات هذا الشهر","New Hires (This Month)",
            "الموظفون المعينون منذ بداية الشهر","Hired since start of month","UserPlus","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count","",
                new[]{ new SemanticMetricFilter("HireDate","GreaterThanOrEqual",RelativeValue:"startOfMonth") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("employees_by_department","الموظفون حسب الإدارة","Employees by Department",
            "توزيع الموظفين على الإدارات","Employee distribution by department","BarChart3","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count",null,Array.Empty<SemanticMetricFilter>(),"DepartmentId"),
            "BarChart", new[]{"BranchId"}),

        new SemanticMetric("gross_payroll","إجمالي الاستحقاقات","Gross Payroll",
            "مجموع الاستحقاقات","Sum of gross earnings","Wallet","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","GrossEarnings",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("net_payroll","صافي الرواتب","Net Payroll",
            "مجموع صافي الرواتب","Sum of net amounts","Wallet","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","NetAmount",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("total_deductions","إجمالي الخصومات","Total Deductions",
            "مجموع الخصومات","Sum of deductions","Wallet","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","TotalDeductions",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId"}),

        new SemanticMetric("late_employees","الموظفون المتأخرون","Late Employees",
            "عدد سجلات التأخير","Count of late attendance records","Clock","attendance",
            new[]{"Attendance.View"},
            new SemanticMetricDefinition("AttendanceRecord","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"6") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("absent_employees","الموظفون الغائبون","Absent Employees",
            "عدد سجلات الغياب","Count of absent attendance records","UserX","attendance",
            new[]{"Attendance.View"},
            new SemanticMetricDefinition("AttendanceRecord","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"2") }, null),
            "KpiCard", new[]{"DepartmentId","BranchId"}),

        new SemanticMetric("overtime_minutes","إجمالي العمل الإضافي (دقائق)","Overtime (Minutes)",
            "مجموع دقائق العمل الإضافي","Sum of overtime minutes","Timer","attendance",
            new[]{"Attendance.View"},
            new SemanticMetricDefinition("AttendanceRecord","Sum","OvertimeMinutes",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", new[]{"DepartmentId"}),

        new SemanticMetric("remaining_leave_balance","رصيد الإجازات المتبقي","Remaining Leave Balance",
            "إجمالي أرصدة الإجازات المتبقية","Total remaining leave days","CalendarCheck","leaves",
            new[]{"Leaves.View"},
            new SemanticMetricDefinition("LeaveBalance","Formula",null,Array.Empty<SemanticMetricFilter>(),null,
                Formula:"m1 + m2 - m3",
                Measures: new[]
                {
                    new SemanticMetricMeasure("m1","Sum","EntitledDays",Array.Empty<SemanticMetricFilter>()),
                    new SemanticMetricMeasure("m2","Sum","CarriedForwardDays",Array.Empty<SemanticMetricFilter>()),
                    new SemanticMetricMeasure("m3","Sum","UsedDays",Array.Empty<SemanticMetricFilter>()),
                }),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("pending_requests","الطلبات المعلقة","Pending Requests",
            "الطلبات بحالة معلق","Requests with Pending status","Inbox","requests",
            new[]{"Requests.View"},
            new SemanticMetricDefinition("RequestInstance","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"1") }, null),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("expiring_contracts","العقود المنتهية قريباً","Expiring Contracts",
            "العقود المنتهية خلال 30 يوماً","Contracts ending within 30 days","FileWarning","employees",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee","Count","",
                new[]
                {
                    new SemanticMetricFilter("ContractEndDate","GreaterThanOrEqual",RelativeValue:"today"),
                    new SemanticMetricFilter("ContractEndDate","LessThanOrEqual",RelativeValue:"today+30d"),
                }, null),
            "KpiCard", new[]{"DepartmentId"}),

        new SemanticMetric("expiring_documents","المستندات المنتهية قريباً","Expiring Documents",
            "المستندات المنتهية خلال 30 يوماً","Documents expiring within 30 days","FileWarning","documents",
            new[]{"Employees.View"},
            new SemanticMetricDefinition("EmployeeDocument","Count","",
                new[]
                {
                    new SemanticMetricFilter("ExpiryDate","GreaterThanOrEqual",RelativeValue:"today"),
                    new SemanticMetricFilter("ExpiryDate","LessThanOrEqual",RelativeValue:"today+30d"),
                }, null),
            "KpiCard", Array.Empty<string>()),

        // Intentionally self-hiding (no backing column / entity) — surface in health as known gaps:
        new SemanticMetric("total_gosi","إجمالي التأمينات","Total GOSI",
            "إجمالي اشتراكات التأمينات","Total GOSI contributions","ShieldCheck","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","GosiAmount",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("total_additions","إجمالي الإضافات","Total Additions",
            "إجمالي الإضافات","Total additions","PlusCircle","payroll",
            new[]{"Payroll.View"},
            new SemanticMetricDefinition("PayrollPayslip","Sum","TotalAdditions",Array.Empty<SemanticMetricFilter>(),null),
            "KpiCard", Array.Empty<string>()),

        new SemanticMetric("pending_approvals","الموافقات المعلقة","Pending Approvals",
            "الموافقات بانتظار القرار","Approvals awaiting decision","CheckSquare","requests",
            new[]{"Requests.View"},
            new SemanticMetricDefinition("RequestApproval","Count","",
                new[]{ new SemanticMetricFilter("Status","Equals",Value:"1") }, null),
            "KpiCard", Array.Empty<string>()),
    };

    // Search synonyms: normalized token -> expansion tokens (already Arabic-normalized where Arabic).
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["راتب"]   = new[]{"payroll","salary","الرواتب"},
            ["payroll"]= new[]{"راتب","salary"},
            ["موظف"]   = new[]{"employee","staff"},
            ["employee"]=new[]{"موظف","staff"},
            ["تاخير"]  = new[]{"late","تأخير"},
            ["late"]   = new[]{"تاخير"},
            ["غياب"]   = new[]{"absent"},
            ["absent"] = new[]{"غياب"},
            ["اجازه"]  = new[]{"leave","vacation","إجازة"},
            ["leave"]  = new[]{"اجازه","vacation"},
        };
}
```

> The implementer MUST author the remaining objects (PayrollPayslip, AttendanceRecord, LeaveBalance, RequestInstance, Loan, the expense entity, EmployeeDocument) with their fields+groups following the Employee example, and keep all 17 metrics exactly as above. Field/permission bindings are the confirmed ones from the spec.

- [ ] **Step 4: Run to verify PASS** (filter `~CatalogRegistryTests`). Expected: all integrity facts PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/SemanticCatalog/CatalogRegistry.cs backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/CatalogRegistryTests.cs
git commit -m "feat(catalog): curated CatalogRegistry (9 domains, objects, 17 metrics, synonyms)"
```

---

## Task 6: CodeDefinedSemanticCatalog provider (TDD)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/SemanticCatalog/CodeDefinedSemanticCatalog.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/CodeDefinedSemanticCatalogTests.cs`

**Interfaces:**
- Consumes: `ISemanticCatalogProvider` + contracts (Task 1); `CatalogRegistry` (Task 5); `ArabicText` (Task 2); `MetricSpecMapper` (Task 4); `IObjectCatalogService` + `CatalogObjectDto`/`CatalogFieldDto` (existing).
- Produces: `CodeDefinedSemanticCatalog : ISemanticCatalogProvider`.

**Provider rules (implement to satisfy the tests):**
- An **object** is visible iff `IObjectCatalogService.GetObject(ObjectCode) != null`. Its **fields** are filtered to those present on the live object (`catalogObj.Fields.Any(f => f.Code == fieldCode)`); a field whose code is absent is dropped (recorded as hidden `Field`).
- A **metric** is visible iff (a) its `Definition.ObjectCode` resolves, AND (b) every field it references — `AggregationField`, each filter `FieldCode`, `GroupByField`, and every measure's `AggregationField`/filter fields — exists on that object. Otherwise hidden with reason `"object '<code>' not found"` or `"field '<code>' not found on '<object>'"`.
- **Permission filter (metrics only):** in the `Get*`/`Search` consumer methods, drop metrics whose `RequiredPermissions` are not all in `ctx.Permissions`. `GetHealth()` ignores permissions.
- **Health:** `GetHealth()` returns counts + every hidden object/field/metric with its reason. Also log a one-line summary + each hidden item at Debug in the constructor (compute the validated view once).
- **Search:** normalize the query with `ArabicText.Normalize`, expand via `CatalogRegistry.Synonyms`, and match against normalized `NameAr`/`NameEn`/`Keywords`/`Code` of visible (validation-passing) objects/fields/metrics; apply permission filter to metric hits; score by match quality (exact code/name > keyword contains); return ordered `SemanticSearchHit`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.SemanticCatalog;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.SemanticCatalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class CodeDefinedSemanticCatalogTests
{
    // Fake catalog: only the objects/fields we declare exist.
    private sealed class FakeCatalog : IObjectCatalogService
    {
        private readonly Dictionary<string, CatalogObjectDto> _objs;
        public FakeCatalog(params CatalogObjectDto[] objs) => _objs = objs.ToDictionary(o => o.Code);
        public IReadOnlyList<CatalogObjectDto> GetCatalog() => _objs.Values.ToList();
        public CatalogObjectDto? GetObject(string code) => _objs.GetValueOrDefault(code);
        public ResolvedObject? Resolve(string code) => null; // not used by the provider
    }

    private static CatalogFieldDto F(string code) => new()
    {
        Code = code, NameEn = code, NameAr = code, FieldType = "Text",
        IsFilterable = true, Fields = null!, // set only the props used
    };

    private static CatalogObjectDto Obj(string code, params string[] fields) => new()
    {
        Code = code, NameEn = code, NameAr = code, Module = "X",
        Fields = fields.Select(F).ToList(),
    };

    // A catalog rich enough for the resolvable metrics under test.
    private static IObjectCatalogService FullCatalog() => new FakeCatalog(
        Obj("Employee", "Status", "HireDate", "DepartmentId", "BranchId", "JobTitleId", "ContractEndDate", "BasicSalary", "FirstNameAr"),
        Obj("PayrollPayslip", "GrossEarnings", "NetAmount", "TotalDeductions"),
        Obj("AttendanceRecord", "Status", "OvertimeMinutes"),
        Obj("LeaveBalance", "EntitledDays", "CarriedForwardDays", "UsedDays"),
        Obj("RequestInstance", "Status"),
        Obj("EmployeeDocument", "ExpiryDate"),
        Obj("Loan"), Obj("Expense"));

    private static CodeDefinedSemanticCatalog Sut(IObjectCatalogService cat)
        => new(cat, NullLogger<CodeDefinedSemanticCatalog>.Instance);

    private static CatalogQueryContext All => new(new[]
        { "Employees.View","Payroll.View","Attendance.View","Leaves.View","Requests.View","Platform.Dashboards.View" });

    [Fact]
    public void Resolvable_metrics_are_visible_self_hiders_are_not()
    {
        var sut = Sut(FullCatalog());
        var codes = sut.GetMetrics(All).Select(m => m.Code).ToList();
        codes.Should().Contain(new[] { "total_employees","net_payroll","remaining_leave_balance","expiring_documents" });
        codes.Should().NotContain(new[] { "total_gosi","total_additions","pending_approvals" });
    }

    [Fact]
    public void Health_reports_self_hidden_metrics_with_reasons()
    {
        var health = Sut(FullCatalog()).GetHealth();
        var hidden = health.Hidden.Where(h => h.Kind == "Metric").Select(h => h.Code).ToList();
        hidden.Should().Contain(new[] { "total_gosi","total_additions","pending_approvals" });
        health.Hidden.Single(h => h.Code == "total_gosi").Reason.Should().Contain("GosiAmount");
    }

    [Fact]
    public void Permission_filter_hides_payroll_metrics_without_permission()
    {
        var sut = Sut(FullCatalog());
        var ctx = new CatalogQueryContext(new[] { "Employees.View" });
        var codes = sut.GetMetrics(ctx).Select(m => m.Code).ToList();
        codes.Should().Contain("total_employees");
        codes.Should().NotContain("net_payroll"); // needs Payroll.View
    }

    [Fact]
    public void Object_missing_from_catalog_is_hidden()
    {
        // Catalog WITHOUT PayrollPayslip → payroll object + its metrics hide.
        var cat = new FakeCatalog(Obj("Employee", "Status", "HireDate", "DepartmentId", "ContractEndDate"));
        var sut = Sut(cat);
        sut.GetObjects(All).Select(o => o.ObjectCode).Should().NotContain("PayrollPayslip");
        sut.GetMetrics(All).Select(m => m.Code).Should().NotContain("net_payroll");
    }

    [Fact]
    public void Search_matches_arabic_and_synonyms()
    {
        var sut = Sut(FullCatalog());
        sut.Search(All, "راتب").Select(h => h.Code).Should().Contain("net_payroll");
        sut.Search(All, "late").Select(h => h.Code).Should().Contain("late_employees");
        sut.Search(All, "تأخير").Select(h => h.Code).Should().Contain("late_employees");
    }
}
```

> Confirm `CatalogObjectDto`/`CatalogFieldDto` construction against `CatalogModels.cs` — set only the properties the provider reads (`Code`, `Fields`, `Fields[].Code`). Adjust the `F`/`Obj` helpers if those DTOs are records with required positional args.

- [ ] **Step 2: Run to verify FAIL** (filter `~CodeDefinedSemanticCatalogTests`). Expected: FAIL — provider not defined.

- [ ] **Step 3: Implement `CodeDefinedSemanticCatalog.cs`** per the provider rules above. Validate once in the constructor (build the visible sets + hidden list from `CatalogRegistry` against `IObjectCatalogService`, using `MetricSpecMapper` only if you choose to double-check field references — field existence checks are enough). Expose `Get*` with permission filtering, `Search` with `ArabicText.Normalize` + synonyms, and `GetHealth`. Log the summary + hidden items at Debug.

- [ ] **Step 4: Run to verify PASS**. Expected: all provider tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/SemanticCatalog/CodeDefinedSemanticCatalog.cs backend/tests/HR.Modules.Platform.Tests/SemanticCatalog/CodeDefinedSemanticCatalogTests.cs
git commit -m "feat(catalog): CodeDefinedSemanticCatalog provider (validate/permission/search/health)"
```

---

## Task 7: Controller + DI registration

**Files:**
- Create: `backend/src/HR.Modules/Platform/Controllers/SemanticCatalogController.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection.cs`

**Interfaces:**
- Consumes: `ISemanticCatalogProvider` (Task 1/6); `ICurrentUserService.Permissions` (existing).

- [ ] **Step 1: Register the provider** in `AddPlatformModule` (near the other Platform service registrations):

```csharp
        services.AddScoped<HR.Application.SemanticCatalog.ISemanticCatalogProvider,
            HR.Modules.Platform.Services.SemanticCatalog.CodeDefinedSemanticCatalog>();
```

- [ ] **Step 2: Create the controller.** Mirror the auth pattern of `ObjectCatalogController` (open it first for the exact base class, route prefix, and `[RequirePermission(...)]` usage — reuse the same attribute type and the any-of `Platform.Dashboards.View`/`Platform.Reports.View` gate; use `Platform.Dashboards.Create` for `/health`). Build `CatalogQueryContext` from `ICurrentUserService.Permissions`.

```csharp
using HR.Application.Common.Interfaces;
using HR.Application.SemanticCatalog;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

[ApiController]
[Route("api/platform/catalog")]
public sealed class SemanticCatalogController : ControllerBase   // match ObjectCatalogController's base if it differs
{
    private readonly ISemanticCatalogProvider _catalog;
    private readonly ICurrentUserService _user;
    public SemanticCatalogController(ISemanticCatalogProvider catalog, ICurrentUserService user)
    { _catalog = catalog; _user = user; }

    private CatalogQueryContext Ctx => new(_user.Permissions);

    // Replace [RequirePermission(...)] with the EXACT attribute + args ObjectCatalogController uses.
    [HttpGet("domains")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetDomains() => Ok(_catalog.GetDomains(Ctx));

    [HttpGet("objects")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetObjects([FromQuery] string? domain) => Ok(_catalog.GetObjects(Ctx, domain));

    [HttpGet("objects/{objectCode}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetObject(string objectCode)
        => _catalog.GetObject(Ctx, objectCode) is { } o ? Ok(o) : NotFound();

    [HttpGet("metrics")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetMetrics([FromQuery] string? domain) => Ok(_catalog.GetMetrics(Ctx, domain));

    [HttpGet("metrics/{metricCode}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetMetric(string metricCode)
        => _catalog.GetMetric(Ctx, metricCode) is { } m ? Ok(m) : NotFound();

    [HttpGet("search")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult Search([FromQuery] string q) => Ok(_catalog.Search(Ctx, q ?? ""));

    [HttpGet("health")]
    [RequirePermission("Platform.Dashboards.Create")]
    public IActionResult Health() => Ok(_catalog.GetHealth());
}
```

- [ ] **Step 3: Build the API** `dotnet build backend/src/HR.Api/HR.Api.csproj -v q` → Build succeeded. (Fix the `[RequirePermission]` attribute name/args and base class to match `ObjectCatalogController` exactly if the build errors.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Modules/Platform/Controllers/SemanticCatalogController.cs backend/src/HR.Modules/Platform/DependencyInjection.cs
git commit -m "feat(catalog): SemanticCatalogController + DI registration"
```

---

## Task 8: Full build + test gate

- [ ] **Step 1:** `dotnet build backend/HR.sln -v q` → 0 errors.
- [ ] **Step 2:** `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --nologo` → all new `SemanticCatalog` tests pass; previously-passing tests unaffected; skipped count unchanged.
- [ ] **Step 3 (commit if any incidental fix):** `git add -A && git commit -m "chore(catalog): full build + test green" || echo "nothing to commit"`

---

## Self-Review

**Spec coverage:**
- Contract + `ISemanticCatalogProvider` abstraction (UI→API→provider→registry) → Task 1, 7. ✅
- Code-defined registry, no migration, no engine change → Tasks 5, 6 (reads `IObjectCatalogService` only). ✅
- `SemanticMetricDefinition` abstract; `WidgetQuerySpec` internal-only via mapper → Tasks 1, 4. ✅
- 9 domains, 7 field groups, objects, 17 metrics (13 resolve, 4 self-hide) → Task 5. ✅
- Self-adapting validation + observable health → Task 6 (+health endpoint Task 7). ✅
- Permission filtering (metrics) → Task 6. ✅
- Arabic normalization + synonyms search → Tasks 2, 6. ✅
- Relative-date + formula metrics → Tasks 3, 4, 5. ✅
- Read-only, scoped provider, no tenant overrides → Tasks 6, 7. ✅
- API surface (domains/objects/metrics/search/health) → Task 7. ✅
- Localization (Ar+En on every item) → Task 5 data. ✅
- Testing per spec → Tasks 2–6. ✅

**Placeholder scan:** The only intentionally-open content is Task 5's "author the remaining objects following the Employee example" — the pattern, exact field bindings, and all 17 metrics are fully specified; the remaining objects are mechanical repetition of the shown shape with the spec's confirmed fields. No `TBD`/`add error handling`/vague steps elsewhere.

**Type consistency:** `SemanticMetricDefinition`/`SemanticMetricFilter`/`SemanticMetricMeasure` fields match across Tasks 1, 4, 5. `MetricSpecMapper.ToWidgetSpec(def, nowUtc)` signature consistent (Tasks 4, 6). Provider method signatures match `ISemanticCatalogProvider` (Tasks 1, 6, 7). `ArabicText.Normalize`/`RelativeDate.Resolve` names consistent (Tasks 2, 3, 4, 6). Permission strings (`Employees.View`, `Payroll.View`, …) consistent (Tasks 5, 6). `ObjectCode`/`FieldCode` tokens match the confirmed entity fields.
