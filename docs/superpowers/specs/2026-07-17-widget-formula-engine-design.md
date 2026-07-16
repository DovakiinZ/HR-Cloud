# Widget Formula Engine — Calculated KPI (SP-3b) — Design

**Date:** 2026-07-17
**Status:** Approved (design)
**Part of program:** Dashboards backlog (SP-3a export ✅ → **SP-3b formula engine** → SP-3c ESS → SP-3d heatmap/calendar).

## Context / current state (verified 2026-07-17)

Widget KPIs today support single aggregations (`Count|Sum|Average|Min|Max|DistinctCount|Percentage`); `Percentage` is a hardcoded numerator÷denominator ratio. This sub-project adds a **Calculated KPI**: a widget defines several **named measures** (each a normal sub-aggregation over the same object) plus a **formula** over those measures, evaluated by the reports AST expression engine. This **generalizes `Percentage`** (m1÷m2×100). **No DB migration** — the spec lives in `DashboardWidget.Configuration` JSONB.

Verified reuse chain (all present):
- `ExpressionParser.Parse(string) → Expr` and `ExpressionParser.TryValidate(string) → string? error` (`HR.Domain.Engines.Finance.Expressions`) — the infix parser reports already reuses.
- `ComputedFieldEvaluator.Evaluate(Expr ast, IReadOnlyDictionary<string,object?> facts) → object?` (`HR.Modules.Platform.Services.Reports`) — evaluates an AST against a facts dict (numbers/strings/bools/null), reusing the Finance `ExpressionEvaluator` + `ReportFunctions` (round/coalesce/etc.).
- `ReportFormulaCompiler.Validate(text) → string? reason` and the existing `POST /api/platform/reports/validate-formula` endpoint — generic text validation the widget builder can reuse for live feedback.
- Widget engine internals (`WidgetDataService`, same class, private): `AggregateExpr(obj, AggKind, field, alias)`, `BaseWhere`, `AppendFilters`, `ScalarAsync(sql, params, ct)`, `TableRef(obj)`, `enum AggKind`, `Scalar(obj, spec, value)`; the scalar path is `ExecuteScalarAsync`. `WidgetQuerySpec` is read from `Configuration` JSONB and also accepted directly by `POST widget-data/preview`.

Design principle: reuse the parser + evaluator (no fork); compute each measure with the existing aggregation SQL; evaluate the formula over the measure results. Scalar (KPI) only this increment.

---

## Component 1 — `WidgetFormulaEvaluator` (pure)

**File:** `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetFormulaEvaluator.cs`

```
public static double Evaluate(string formula, IReadOnlyDictionary<string, double> measures)
```
- Parse `formula` via `ExpressionParser.Parse` (throws `ExpressionException` on a bad formula — the caller wraps it into a `ValidationException`).
- Build facts = `measures` projected to `object?` values; evaluate via `new ComputedFieldEvaluator().Evaluate(ast, facts)`.
- Return `result is null ? 0d : Convert.ToDouble(result, InvariantCulture)`.
- Also expose `public static string? Validate(string formula) => ReportFormulaCompiler.Validate(formula);` (thin reuse) so a widget-scoped validator exists if needed.

Pure, DB-free unit tests: `Evaluate("m1 / m2 * 100", {m1:3, m2:12}) == 25`; `Evaluate("round(a + b, 0)", {a:1.4, b:1.4}) == 3` (round(2.8,0)); division-by-zero and unknown-variable behavior asserted (unknown variable → the evaluator resolves it to null/0 per the engine's semantics — assert the actual behavior, do not assume).

## Component 2 — Spec model: named measures + formula

**File:** `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataModels.cs` (extend `WidgetQuerySpec`)

Add to `WidgetQuerySpec`:
```csharp
public string? Formula { get; set; }                       // used when Aggregation == "Formula"
public List<WidgetMeasureSpec> Measures { get; set; } = new();
```
Add:
```csharp
public sealed class WidgetMeasureSpec
{
    public string Name { get; set; } = null!;              // variable name referenced by Formula (e.g. "m1")
    public string Aggregation { get; set; } = "Count";     // Count|Sum|Average|Min|Max|DistinctCount
    public string? AggregationField { get; set; }
    public List<WidgetFilterSpec> Filters { get; set; } = new();
}
```
Stored in `Configuration` JSONB — **no migration**. Existing widgets deserialize with empty `Measures`/null `Formula` (backward-compatible).

## Component 3 — Engine path: `ExecuteFormulaScalarAsync`

**File:** `backend/src/HR.Modules/Platform/Services/WidgetData/WidgetDataService.cs` (add a private method + one dispatch branch)

- **Dispatch:** in the method that routes scalar vs series (where `Aggregation`/`GroupByField` decide), add: if `spec.Aggregation` equals `"Formula"` (case-insensitive) **and** `GroupByField` is null/empty → call `ExecuteFormulaScalarAsync(obj, spec, filters, ct)`. (Formula is a scalar concept — if a `GroupByField` is set, fall back to treating it as `Count`, mirroring how `Percentage` degrades in `ExecuteSeriesAsync`.)
- **`ExecuteFormulaScalarAsync`:**
  - Validate: `spec.Measures` non-empty and `spec.Formula` non-blank, else `Invalid("formula", "A calculated widget needs measures and a formula.")` (reuse the service's existing `Invalid(...)` helper → surfaces as 400).
  - For each `WidgetMeasureSpec m`:
    - Resolve `AggKind` from `m.Aggregation` (reuse the same parse the service already uses for `spec.Aggregation`; unknown → `Invalid`).
    - `var p = new Params(); var where = BaseWhere(obj, "t", p); AppendFilters(where, obj, MergeFilters(filters, m.Filters), "t", p);` (merge the dashboard/widget filters already passed in with the measure's own filters).
    - `var aggExpr = AggregateExpr(obj, aggKind, m.AggregationField, "t");`
    - `var v = await ScalarAsync($"SELECT {aggExpr} FROM {TableRef(obj)} t {Where(where)}", p, ct);`
    - `measures[m.Name] = v is null or DBNull ? 0d : Convert.ToDouble(v);`
  - `var value = WidgetFormulaEvaluator.Evaluate(spec.Formula!, measures);` wrapped in try/catch on `ExpressionException` → `Invalid("formula", ex.Message)`.
  - `return Scalar(obj, spec, value);`
  - `MergeFilters(a, b)` = a small local concat of the two filter lists (measure filters last so they refine).
- `[SkippableFact]` (gated `REPORTS_TEST_DB`): seed a tiny object + rows, a Formula spec with two Count measures (one filtered) + `m1 / m2 * 100`, execute via `ExecuteAsync(spec, null, ct)`, assert the scalar equals the expected ratio.

## Component 4 — Frontend: calculated mode in the widget builder

**File:** `src/components/dashboard/widget-builder.tsx` (+ `src/lib/api/dashboards.ts` if a validate call helper is needed)

- Add **"محسوب / Calculated"** to the aggregation choices. When selected, the builder hides the single aggregation-field picker and shows a **measures editor**: a small repeatable list where each row is `{ name, aggregation (Count/Sum/Avg/Min/Max/DistinctCount), field?, optional filter }`, plus a **formula** textarea referencing the measure names, live-validated via the existing `POST /api/platform/reports/validate-formula` (debounced; green tick / red error). The spec sent to **preview** (`widget-data/preview`) and saved to the widget carries `aggregation: "Formula"`, `measures: [...]`, `formula: "..."`.
- Keep every existing aggregation mode unchanged. Only KPI/scalar visualizations offer Calculated (a GroupBy + Formula is not supported this increment — hide GroupBy when Calculated is chosen, or ignore it).
- Reuse the builder's existing live-preview panel to render the resulting KPI.

---

## Testing & gates
- **Backend:** TDD — `WidgetFormulaEvaluator` DB-free tests first. `dotnet build backend/src/HR.Api/HR.Api.csproj` = 0 errors; `dotnet test backend/tests/HR.Modules.Platform.Tests` green (evaluator tests pass; engine `[SkippableFact]` skipped locally).
- **Frontend:** `npx next build` = 0 errors.
- **Deploy:** backend zip-deploy once, push → Vercel auto-deploys FE. No migration.
- Live-verify: a Calculated KPI widget with `m1=Count(filtered)`, `m2=Count(all)`, `formula=m1/m2*100` previews the expected percentage; save + reopen keeps the measures/formula.

## Known limits carried forward
- Scalar (KPI) formulas only — grouped/series formulas and formula-over-table-columns are out of scope (a later increment; SP-3b chose the KPI model).
- Measures reference the widget's single primary object (no cross-object measures — consistent with the widget engine's single-object model).
- The formula variable namespace is the measure `Name`s; a formula referencing an undefined name resolves per the engine's default (asserted in tests, surfaced as 0/null — the builder's live validation guides the user).

## Self-review
- No placeholders; each component names its file, signatures, and behavior.
- Consistent: reuses `ExpressionParser`/`ComputedFieldEvaluator`/`ReportFormulaCompiler` + the widget engine's own scalar-aggregation helpers; no fork; no migration (JSONB spec).
- Scope: one implementation plan (pure evaluator + model + engine path + builder mode). ESS + heatmap are separate sub-projects.
- Ambiguity resolved: Formula is scalar-only (degrades to Count if grouped); each measure is a normal sub-aggregation with its own filters merged after the dashboard/widget filters; evaluation reuses the reports AST evaluator over a measures-as-facts dict.
