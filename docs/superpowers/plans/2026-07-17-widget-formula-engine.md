# Widget Formula Engine — Calculated KPI (SP-3b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Calculated KPI widget — named measures + a formula over them — evaluated by the reports AST expression engine.

**Architecture:** A pure `WidgetFormulaEvaluator` (reuses `ExpressionParser` + `ComputedFieldEvaluator`) evaluates a formula over a `{measureName: value}` dict. `WidgetQuerySpec` gains `Measures` + `Formula` (JSONB, no migration). `WidgetDataService` computes each measure with its existing scalar-aggregation SQL, then evaluates the formula. The widget builder gains a Calculated mode.

**Tech Stack:** .NET 8, EF Core 8, xUnit + FluentAssertions; Next.js 16.2.6 + TypeScript. Reuses `HR.Domain.Engines.Finance.Expressions` (`ExpressionParser`, `Expr`), `ComputedFieldEvaluator`, `ReportFormulaCompiler`, and `WidgetDataService`'s private aggregation helpers.

## Global Constraints

- **No DB migration.** `Measures`/`Formula` live in `DashboardWidget.Configuration` JSONB; existing widgets deserialize with empty `Measures`/null `Formula`.
- **Reuse, do not fork:** `ExpressionParser.Parse(string) → Expr` / `ExpressionParser.TryValidate(string) → string?` (`HR.Domain.Engines.Finance.Expressions`); `ComputedFieldEvaluator.Evaluate(Expr, IReadOnlyDictionary<string,object?>) → object?` (`HR.Modules.Platform.Services.Reports`); `ReportFormulaCompiler.Validate(text) → string?`. In `WidgetDataService` (same class), reuse private `AggregateExpr(obj, AggKind, field, alias)`, `BaseWhere`, `AppendFilters`, `ScalarAsync`, `TableRef`, `Where`, `Scalar`, `Combine`, `ParseAggregation`, `Invalid`, `Resolve`, `enum AggKind`, `class Params`.
- **Formula is scalar-only.** Dispatch to the formula path only when `Aggregation == "Formula"` (case-insensitive) AND `GroupByField` is null/blank.
- **Gates:** `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` green; `npx next build` = 0 errors. DB-touching tests `[SkippableFact]` gated on `REPORTS_TEST_DB`. Commit after each task.

---

## Task 1: `WidgetFormulaEvaluator` (pure) + tests

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetFormulaEvaluator.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetFormulaEvaluatorTests.cs`

**Interfaces:**
- Consumes: `ExpressionParser`, `Expr` (`HR.Domain.Engines.Finance.Expressions`); `ComputedFieldEvaluator` (`HR.Modules.Platform.Services.Reports`); `ReportFormulaCompiler`.
- Produces: `static double WidgetFormulaEvaluator.Evaluate(string formula, IReadOnlyDictionary<string,double> measures)`; `static string? WidgetFormulaEvaluator.Validate(string formula)`.

- [ ] **Step 1: Write the failing tests** `WidgetFormulaEvaluatorTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.Dashboards;

public class WidgetFormulaEvaluatorTests
{
    [Fact]
    public void Ratio_formula_over_measures()
        => WidgetFormulaEvaluator.Evaluate("m1 / m2 * 100", new Dictionary<string, double> { ["m1"] = 3, ["m2"] = 12 })
            .Should().Be(25d);

    [Fact]
    public void Round_function_is_available()
        => WidgetFormulaEvaluator.Evaluate("round(a + b, 0)", new Dictionary<string, double> { ["a"] = 1.4, ["b"] = 1.4 })
            .Should().Be(3d); // round(2.8, 0)

    [Fact]
    public void Valid_formula_returns_null_reason()
        => WidgetFormulaEvaluator.Validate("m1 / m2").Should().BeNull();

    [Fact]
    public void Invalid_formula_returns_reason()
        => WidgetFormulaEvaluator.Validate("m1 / / m2").Should().NotBeNull();
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~WidgetFormulaEvaluatorTests` → FAIL (type missing).

- [ ] **Step 3: Implement** `WidgetFormulaEvaluator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HR.Domain.Engines.Finance.Expressions;
using HR.Modules.Platform.Services.Reports;

namespace HR.Modules.Platform.Services.WidgetData;

/// <summary>Evaluates a Calculated KPI formula over named measure values, reusing the reports
/// expression engine (ExpressionParser → ComputedFieldEvaluator). Pure and deterministic.</summary>
public static class WidgetFormulaEvaluator
{
    public static double Evaluate(string formula, IReadOnlyDictionary<string, double> measures)
    {
        Expr ast = ExpressionParser.Parse(formula); // throws ExpressionException on a bad formula
        var facts = measures.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        var result = new ComputedFieldEvaluator().Evaluate(ast, facts);
        return result is null ? 0d : Convert.ToDouble(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Null when the formula parses, else the reason (for the builder's live validation).</summary>
    public static string? Validate(string formula) => ReportFormulaCompiler.Validate(formula);
}
```

> If `ExpressionParser.Parse` returns a type other than `Expr` (e.g. a wrapper), adapt the local type — `ComputedFieldEvaluator.Evaluate`'s first parameter type is the source of truth (it is `Expr` per `ComputedFieldEvaluator.cs`). If `Convert.ToDouble` on a `decimal`/`string` result needs care, note that `ComputedFieldEvaluator.ToClr` returns `decimal`/`string`/`bool`/`null`; `Convert.ToDouble` handles `decimal` and numeric strings.

- [ ] **Step 4: Run to verify it passes** — same filter → PASS (4 tests). If `round(2.8,0)` yields `3` (AwayFromZero, per `ReportFunctions`) the test holds; if the engine's round differs, adjust the expected value to the actual engine behavior and note it.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/WidgetData/WidgetFormulaEvaluator.cs backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetFormulaEvaluatorTests.cs
git commit -m "feat(dashboards): pure widget formula evaluator (measures -> AST -> scalar)"
```

---

## Task 2: Spec model + engine formula path

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataModels.cs`
- Modify: `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataService.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetFormulaExecutionTests.cs`

**Interfaces:**
- Consumes: `WidgetFormulaEvaluator.Evaluate` (Task 1); `WidgetDataService` private helpers.
- Produces: `WidgetQuerySpec.{Formula, Measures}`, `WidgetMeasureSpec`; `WidgetDataService.ExecuteFormulaScalarAsync`.

- [ ] **Step 1: Extend the model** in `WidgetDataModels.cs`. Add to `WidgetQuerySpec` (after `Filters`):

```csharp
    public string? Formula { get; set; }                    // used when Aggregation == "Formula"
    public List<WidgetMeasureSpec> Measures { get; set; } = new();
```
Add the class (next to `WidgetFilterSpec`):
```csharp
public sealed class WidgetMeasureSpec
{
    public string Name { get; set; } = null!;               // variable referenced by Formula (e.g. "m1")
    public string Aggregation { get; set; } = "Count";      // Count|Sum|Average|Min|Max|DistinctCount
    public string? AggregationField { get; set; }
    public List<WidgetFilterSpec> Filters { get; set; } = new();
}
```

- [ ] **Step 2: Add the dispatch branch** in `WidgetDataService.ExecuteAsync` (lines ~35-44). Replace the body with (moves `filters` up + adds the formula branch):

```csharp
    public async Task<WidgetDataResult> ExecuteAsync(WidgetQuerySpec spec, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct)
    {
        var obj = Resolve(spec.ObjectCode);
        var filters = Combine(spec.Filters, dashboardFilters);

        if (string.Equals(spec.Aggregation, "Formula", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(spec.GroupByField))
            return await ExecuteFormulaScalarAsync(obj, spec, filters, ct);

        var agg = ParseAggregation(spec.Aggregation);
        if (string.IsNullOrWhiteSpace(spec.GroupByField))
            return await ExecuteScalarAsync(obj, agg, spec, filters, ct);
        return await ExecuteSeriesAsync(obj, agg, spec, filters, ct);
    }
```
(`obj`, `Combine`, `ParseAggregation`, `ExecuteScalarAsync`, `ExecuteSeriesAsync` are unchanged; only the `Formula` branch + the reordered `filters` line are new. Add `using System;` if not already present for `StringComparison`.)

- [ ] **Step 3: Add `ExecuteFormulaScalarAsync`** in `WidgetDataService.cs` (place it right after `ExecuteScalarAsync`):

```csharp
    // ── Calculated KPI (formula over named measures) ─────────────────────────
    private async Task<WidgetDataResult> ExecuteFormulaScalarAsync(ResolvedObject obj, WidgetQuerySpec spec, List<WidgetFilterSpec> filters, CancellationToken ct)
    {
        if (spec.Measures.Count == 0 || string.IsNullOrWhiteSpace(spec.Formula))
            throw Invalid("formula", "A calculated widget needs at least one measure and a formula.");

        var table = TableRef(obj);
        var measures = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in spec.Measures)
        {
            if (string.IsNullOrWhiteSpace(m.Name)) throw Invalid("measure", "Each measure needs a name.");
            var aggKind = ParseAggregation(m.Aggregation);
            if (aggKind == AggKind.Percentage) throw Invalid("measure", "A measure cannot itself be a percentage/formula.");

            var p = new Params();
            var where = BaseWhere(obj, "t", p);
            var merged = new List<WidgetFilterSpec>(filters);
            merged.AddRange(m.Filters);
            AppendFilters(where, obj, merged, "t", p);
            var aggExpr = AggregateExpr(obj, aggKind, m.AggregationField, "t");
            var v = await ScalarAsync($"SELECT {aggExpr} FROM {table} t {Where(where)}", p, ct);
            measures[m.Name] = v is null or DBNull ? 0d : Convert.ToDouble(v);
        }

        double value;
        try { value = WidgetFormulaEvaluator.Evaluate(spec.Formula!, measures); }
        catch (HR.Domain.Engines.Finance.Expressions.ExpressionException ex) { throw Invalid("formula", $"Invalid formula: {ex.Message}"); }

        return Scalar(obj, spec, value);
    }
```

> Verify the private helper names against the file: `BaseWhere(obj, "t", p)`, `AppendFilters(where, obj, filters, "t", p)`, `AggregateExpr(obj, aggKind, field, "t")`, `ScalarAsync(sql, p, ct)`, `Where(where)`, `TableRef(obj)`, `Scalar(obj, spec, value)`, `Invalid(field, msg)`, `enum AggKind`, `class Params` — all confirmed present in `WidgetDataService.cs`. `ExpressionException` is in `HR.Domain.Engines.Finance.Expressions`.

- [ ] **Step 4: Add a `[SkippableFact]`** `WidgetFormulaExecutionTests.cs` — seed a discoverable object (or reuse an existing seeded object like `Employee`) with a few rows, build a `WidgetQuerySpec { ObjectCode=..., Aggregation="Formula", Measures=[ {Name="m1",Aggregation="Count",Filters=[one filter]}, {Name="m2",Aggregation="Count"} ], Formula="m1 / m2 * 100" }`, call `service.ExecuteAsync(spec, null, ct)`, assert `result.Kind=="scalar"` and `result.Value` equals the expected ratio. Gate with `Skip.If(string.IsNullOrWhiteSpace(Conn), ...)`. If seeding a runnable widget object is impractical in the harness, assert instead (DB-free) that `ExecuteFormulaScalarAsync` validation throws `Invalid` for empty measures — construct is awkward for a private method, so prefer testing via the public `ExecuteAsync` with a stub; if neither is clean, note the e2e deferred (the Task 1 evaluator tests are the required pure coverage).

- [ ] **Step 5: Build + test** — `dotnet build backend/src/HR.Api/HR.Api.csproj` (0 errors); `dotnet test backend/tests/HR.Modules.Platform.Tests` (green; e2e skipped locally).

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataModels.cs backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataService.cs backend/tests/HR.Modules.Platform.Tests/Dashboards/WidgetFormulaExecutionTests.cs
git commit -m "feat(dashboards): calculated KPI engine path (named measures + formula)"
```

---

## Task 3: Frontend — Calculated mode in the widget builder

**Files:**
- Modify: `src/types/dashboard.ts` (types)
- Modify: `src/components/dashboard/widget-builder.tsx`

**Interfaces:**
- Consumes: the existing builder spec/preview flow; `POST /api/platform/reports/validate-formula` for live validation.
- Produces: a Calculated aggregation mode that emits `{ aggregation: "Formula", measures: [...], formula: "..." }` in the widget spec.

- [ ] **Step 1: Extend the types** in `src/types/dashboard.ts`:
  - Add `"Formula"` to the `AggregationName` union.
  - Add `export interface WidgetMeasure { name: string; aggregation: AggregationName; aggregationField?: string | null; filters?: WidgetFilter[] }` (reuse the existing `WidgetFilter` type; if filters are complex, `filters` may be omitted for the first version — measures without per-measure filters still express most KPIs).
  - Add `measures?: WidgetMeasure[]` and `formula?: string` to `WidgetQuerySpec`.

- [ ] **Step 2: Add the Calculated mode** to `src/components/dashboard/widget-builder.tsx`. Read the file first — it has an `AGGREGATIONS` array (`{value,label,needsField,measureOnly}`), `aggregation`/`aggField`/`groupBy` state, and a `spec` `useMemo` that builds the `WidgetQuerySpec`. Do:
  - Add `{ value: "Formula", label: "محسوب (صيغة)", needsField: false, measureOnly: false }` to `AGGREGATIONS`.
  - Add state: `const [measures, setMeasures] = useState<WidgetMeasure[]>([{ name: "m1", aggregation: "Count", aggregationField: null }]);` and `const [formula, setFormula] = useState("");` and `const [formulaError, setFormulaError] = useState<string | null>(null);`.
  - When `aggregation === "Formula"`: hide the single aggregation-field picker and the group-by (a Formula KPI is scalar); render a **measures editor** — a list where each row edits `name`, `aggregation` (a sub-select of Count/Sum/Average/Min/Max/DistinctCount), and `aggregationField` (shown when that sub-aggregation needs a field, i.e. not Count) — with add/remove row; and a **formula** `<textarea>` (dir="ltr", monospace) whose `onChange` debounce-calls `validateFormula(formula)` (add a helper in `dashboards.ts` that POSTs `/api/platform/reports/validate-formula` with `{ formula }` and returns `{ isValid, error? }`, mirroring the reports client) and sets `formulaError`.
  - Extend the `spec` `useMemo`: when `aggregation === "Formula"`, include `measures` and `formula` in the emitted `WidgetQuerySpec` (and set `aggregationField`/`groupByField` to null). Keep all other modes exactly as today.
  - In the load path (where an existing widget's spec hydrates the state — `setAggregation(s.spec.aggregation)` etc.), also `setMeasures(s.spec.measures ?? [default])` and `setFormula(s.spec.formula ?? "")`.
  - The existing live-preview panel already POSTs the spec to `widget-data/preview`; a Formula spec previews the KPI with no extra wiring. Keep the preview button/flow.
  - Restrict visualization to KPI/Gauge for Formula (mirror how `Percentage` restricts viz).

- [ ] **Step 3: Build** — `npx next build` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/types/dashboard.ts src/components/dashboard/widget-builder.tsx src/lib/api/dashboards.ts
git commit -m "feat(dashboards): widget builder Calculated (formula) mode with measures + live validation"
```

---

## Final verification & deploy
- [ ] `dotnet build backend/src/HR.Api/HR.Api.csproj` → 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` → green.
- [ ] `npx next build` → 0 errors.
- [ ] Deploy backend once: `dotnet publish backend/src/HR.Api -c Release -o ./publish`, zip forward-slash entries (Python `zipfile`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path <zip> --type zip`. Push → Vercel auto-deploys FE. No migration.
- [ ] Live-verify: build a Calculated KPI (`m1=Count` with a filter, `m2=Count`, `formula=m1/m2*100`) → preview shows the percentage; save + reopen keeps measures/formula; `POST widget-data/preview` with a Formula spec returns the scalar.

## Self-review notes (author)
- Spec Component 1 (evaluator) → Task 1; Component 2 (model) + Component 3 (engine) → Task 2; Component 4 (builder) → Task 3. All covered.
- No migration (JSONB spec); reuses `ExpressionParser`/`ComputedFieldEvaluator`/`ReportFormulaCompiler` + `WidgetDataService` private helpers; no fork.
- Type consistency: `WidgetMeasureSpec{Name,Aggregation,AggregationField?,Filters}` (BE) ↔ `WidgetMeasure{name,aggregation,aggregationField?,filters?}` (FE); dispatch on `Aggregation=="Formula"` + no GroupBy; measure aggregation reuses `ParseAggregation` (Percentage measure rejected); formula errors → `Invalid` (400).
- Known limits (carried): scalar-only formulas; single-object measures; measure filters optional in the first FE version.
