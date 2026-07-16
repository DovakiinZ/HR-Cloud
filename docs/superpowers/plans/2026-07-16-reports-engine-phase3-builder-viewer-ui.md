# Reports Engine Phase 3 — Builder, Viewer & List UI (full R1 close-out)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Checkbox discipline:** Phase 1 and Phase 2 shipped with every checkbox left unticked, so the plans read as "nothing done" when the work was complete. **Tick each box as you go.** A task is done when its commit exists.

**Date:** 2026-07-16
**Status:** Ready to execute.
**Parent spec:** `docs/superpowers/specs/2026-07-12-customizable-reports-engine-design.md` (R1)
**Builds on (shipped):** Phase 1 (execution engine, `2026-07-12-reports-engine-phase1-execution.md`), Phase 2 (access/sharing/organization, `2026-07-14-reports-engine-phase2-sharing-organization.md`), plus the export work (Excel/CSV/PDF writers + `/export` endpoint + list page) that landed after Phase 2 without its own plan.

**Goal:** Close out R1 — ship the **builder**, the **viewer**, and the **enriched list page**, plus the backend gaps that block them.

---

## Why this plan has a backend half

The R1 spec (§8) describes the remaining work as frontend. It isn't. Five backend gaps block spec'd UI, discovered by reading the code rather than the docs:

| # | Gap | Blocks |
|---|---|---|
| 1 | **Joins cannot be created over HTTP.** `ReportRelationship` entity + `ReportRelationshipDto` + EF config all exist, but there is **no controller endpoint** and no command. | Builder "add joins" step |
| 2 | **Computed fields store AST JSON, not formula text.** `ReportField.CalculationExpression` is fed straight to `AstJson.Deserialize`. There is no authoring path from a formula string, and AST-only storage is not round-trippable back into an editable box. | Builder "computed formula input" |
| 3 | **Runtime parameters do not exist.** `ReportFilter.IsParameter` is stored and returned in the DTO but `ReportObjectResolver` ignores it entirely — every filter's stored `Value` goes into SQL. `POST {id}/run` takes no body. | Viewer parameter inputs |
| 4 | **`ReportDefinitionDto` drops `FolderId`, tags, and shares** even though the entity has them and `GetReportsQueryHandler` `Include`s `Shares`. | List page folder/tag state |
| 5 | **Field catalog is gated on `Platform.Dashboards.View`.** A Reports-only user cannot load the builder's field list. | Builder, for non-dashboard users |

### A security prerequisite, not a feature

**`ReportSqlBuilder` applies tenant and soft-delete filters only to the primary table** (`ReportSqlBuilder.cs:38-39`, using `model.PrimaryAlias`). Joined tables get **no tenant predicate**. This is latent today *only because joins cannot be created over HTTP* — gap #1 is what keeps it dormant. **Task A2 opens exactly that door.** Shipping join endpoints without fixing the predicate converts a sleeping bug into a live cross-tenant data leak: a user joins any tenant-scoped object and reads other tenants' rows.

**Task A0 fixes the predicate and must merge before A2.** Do not reorder these.

---

## Global Constraints

- **Reuse, do not rebuild.** `ExpressionParser` (a complete infix parser) already exists — **do not write a parser**. `RequirePermissionAttribute` already has OR/`anyOf` semantics — **do not modify the attribute**. `Platform.Reports.*` permissions are already seeded and backfilled — **no permission seeding work in this plan**.
- **No new permission strings.** Everything gates on the existing `Platform.Reports.View|Create|Edit|Delete|Export`.
- **Namespaces:** services `HR.Modules.Platform.Services.Reports`; commands `HR.Modules.Platform.Commands.Reports`; queries `HR.Modules.Platform.Queries.Reports`; entities `HR.Domain.Engines.Reports`; EF configs `HR.Infrastructure.Persistence.Configurations.Engines`.
- **Tests:** pure logic gets DB-free xUnit tests. DB-touching tests use `[SkippableFact]` gated on `REPORTS_TEST_DB` (mirror `ReportExecutionIntegrationTests`). Test project: `backend/tests/HR.Modules.Platform.Tests`, folder `Reports/`.
- **Commit after each task.** Conventional commits: `feat(reports):` / `fix(reports):` / `test(reports):`.
- **DB apply deferred to deployment.** Generate migrations; do not run `database update` against Azure Postgres here.

### Frontend constraints — three spec premises that do not hold

The R1 spec §8 assumes infrastructure this codebase does not have. Verified by reading `src/`:

1. **There is no i18n system.** No `useTranslation`, no dictionary, no language switcher, nothing in `package.json`. The app is **hardcoded Arabic + RTL** at the root (`<html lang="ar" dir="rtl">`). "Bilingual" is a property of the *data* (`nameAr`/`nameEn` pairs resolved with `||` at the render site), not the UI. **Write Arabic literals inline in JSX.** Do not introduce a translation layer — it would be the only one in the codebase.
2. **There is no shared wizard/stepper.** The only real multi-step wizard is `src/components/workflows/wizard/ApprovalWorkflowWizard.tsx`, whose `Stepper` is a private 9-line local component and whose step state is a plain `useState<1|2>`. **Copy its idioms; there is nothing to import.** Our builder has 6 steps, so Task B4 generalizes `phase` to a step index — new ground, budgeted accordingly.
3. **There is no data-table and no grouping component.** Zero `DataTable` hits, no TanStack Table. `ui/table.tsx` is dumb presentational markup. Pagination is hand-rolled per component. **Grouped rendering is net-new** (Task B3).

Also: **there is no `Select` primitive.** Use a raw `<select>` with the codebase's class string, or `Combobox` (`src/components/ui/combobox.tsx`, single-select only) for entity pickers.

### Frontend gotchas that will bite

- **Three incompatible pagination envelopes coexist.** Reports/dashboards use `pageNumber`/`totalCount`/`totalPages`; payroll uses `page`/`total`. **Stay in the reports family. Never import `Paged<T>` from `payroll.ts`** — it silently yields `undefined` totals.
- **`apiFetch` already toasts 401/403/5xx.** Guard every caller toast with the `notifyError` helper or you will double-toast:
  ```ts
  function notifyError(err: unknown, fallback: string) {
    if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
      toast.error(err instanceof ApiError ? err.message : fallback);
    }
  }
  ```
- **Blob downloads bypass `apiFetch`** (raw bytes, not the JSON envelope) — fetch directly with `API_BASE_URL` + `getAccessToken()`. See the existing `exportReport`.
- **RTL inverts icon direction**: `ArrowRight` = back, `ChevronRight` = previous, `ChevronLeft` = next.
- **`node_modules` is absent.** Run `npm install` before frontend work.
- **Next 16 async params**: `{ params }: { params: Promise<{ id: string }> }` + `const { id } = use(params);`.

---

## Task order

**Part A (backend) → Part B (frontend).** Within Part A, **A0 before A2** is mandatory (security). A1/A3/A4/A5 are independent of each other.

| # | Task | Why |
|---|---|---|
| A0 | Tenant-filter joined tables | **Security prerequisite for A2** |
| A1 | Catalog permission fix | Unblocks builder for reports-only users |
| A2 | Relationship (join) endpoints | Builder step 2 |
| A3 | Formula authoring path | Builder step 3 |
| A4 | Runtime parameters | Viewer parameter inputs |
| A5 | DTO enrichment | List page folder/tag state |
| B1 | API client extension | All FE |
| B2 | List page enrichment | Spec §8 list |
| B3 | Viewer | Spec §8 viewer |
| B4 | Builder wizard | Spec §8 builder |

---

# PART A — Backend enablement

## Task A0: Tenant-filter joined tables (security prerequisite)

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/ReportSqlBuilder.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportSqlBuilderTests.cs` (exists — extend)

**Problem:** `ReportSqlBuilder` emits the tenant and soft-delete predicates against `model.PrimaryAlias` only. Every joined table is unfiltered. `ReportJoinModel` already carries `Target` (a `ResolvedObject` with `HasTenant` / `HasSoftDelete`) and `Alias`, so the fix needs no new data.

**Rule:** for each join whose `Target.HasTenant` is true, add `AND {alias}."TenantId" = @tenantId`; for each whose `Target.HasSoftDelete` is true, add `AND {alias}."IsDeleted" = false`.

> **LEFT JOIN semantics:** putting a joined table's predicate in `WHERE` degrades a LEFT JOIN to an INNER JOIN (the null row fails the predicate). For `left`/`right` joins the predicate **must go in the `ON` clause**, not `WHERE`. For inner joins either works; prefer `ON` uniformly so the emitted SQL is consistent and the LEFT-JOIN trap can never reappear.

- [x] **Step 1: Write the failing test**

Assert the built SQL contains a tenant predicate for a joined tenant-scoped target, and that it sits in the `ON` clause. Follow the existing `ReportSqlBuilderTests` construction of `ReportQueryModel` + `ReportJoinModel`. Cover: (a) inner join to a tenant-scoped object → predicate present; (b) left join → predicate in `ON`, and the SQL still reads `LEFT JOIN`; (c) join to a non-tenant object → no predicate.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportSqlBuilderTests`
Expected: FAIL — no tenant predicate emitted for joins.

- [x] **Step 3: Implement**

In the join loop (`ReportSqlBuilder.cs:28-34`), after the existing `ON {alias}.{key} = {sourceAlias}.{col}` condition, append the target's scope predicates to the same `ON` clause. Reuse whatever helper emits the primary table's tenant/soft-delete predicate so the two paths cannot drift; if it is inlined, extract it to a `private static void AppendScopePredicates(StringBuilder sb, ResolvedObject obj, string alias)` and call it from both.

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportSqlBuilderTests`
Expected: PASS, and all pre-existing SQL-builder tests still green (the primary-table SQL must be byte-identical).

- [x] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportSqlBuilder.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportSqlBuilderTests.cs
git commit -m "fix(reports): tenant/soft-delete filter joined tables (prerequisite for join endpoints)"
```

---

## Task A1: Allow `Platform.Reports.View` on the object catalog

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Controllers/ObjectCatalogController.cs`

`RequirePermissionAttribute` already takes `params string[]` with **OR semantics** (`_permissions.Any(p => currentUser.Permissions.Contains(p))`). Precedent: `EmployeesController.cs:73` → `[RequirePermission("Employees.Terminate", "Employees.ViewSettlement")]`. No attribute change, no seeding, no migration — `Platform.Reports.View` is already seeded (`SeedData.cs:43`) and backfilled to system roles.

- [x] **Step 1: Change the three attributes**

On `GetObjects`, `GetObject`, and `GetFields`:

```csharp
[RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
```

- [x] **Step 2: Build**

Run: `dotnet build backend/src/HR.Api/HR.Api.csproj`
Expected: BUILD succeeds.

- [x] **Step 3: Commit**

```bash
git add backend/src/HR.Modules/Platform/Controllers/ObjectCatalogController.cs
git commit -m "feat(reports): allow Platform.Reports.View on the object catalog registry endpoints"
```

---

## Task A2: Relationship (join) endpoints

**Depends on A0.** Do not start until A0 is committed.

**Files:**
- Create: `backend/src/HR.Modules/Platform/Commands/Reports/ReportRelationshipCommands.cs`
- Create: `backend/src/HR.Modules/Platform/Queries/Reports/ReportRelationshipQueries.cs`
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`
- Modify: `backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs` (`ReportRelationship → ReportRelationshipDto` — verify whether it already exists before adding)
- Modify: `backend/src/HR.Modules/Platform/Validators/ReportValidators.cs`
- Modify: `backend/src/HR.Modules/Platform/Commands/Reports/ReportCommands.cs` (clone fix)
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportRelationshipCommandTests.cs`

**Entity (exists):**
```csharp
public class ReportRelationship : BaseEntity {
    Guid ReportDefinitionId; Guid SourceObjectId; Guid TargetObjectId;
    string JoinField;                     // a field on the SOURCE
    string JoinType = "Inner";            // string, NOT an enum: Inner | Left | Right
    int SortOrder;
    ReportDefinition ReportDefinition; }
```

**Semantics the endpoint must respect** (from `ReportObjectResolver.cs:23-58`):
- The join is always **FK→PK**: `ON target.<KeyColumn> = source.<JoinField column>`. Arbitrary ON clauses are not expressible.
- `JoinField` must be a field of the **Source** object, not the Target (validated at resolver line 47-48).
- **`JoinType` is unvalidated today** — `ReportSqlBuilder.cs:30` does `switch { "left" => ..., "right" => ..., _ => "INNER JOIN" }`, so typos and nulls silently become INNER JOIN. **Validate it.**
- **Alias assignment is `SortOrder`-dependent.** `aliasByObjectId` fills as the loop walks in `SortOrder` order, and `sourceAlias` falls back to `"t0"` via `GetValueOrDefault`. A relationship whose Source is another relationship's Target **must sort after it**, or it silently joins the primary table instead of erroring. **Validate: Source must be the primary object, or a Target already introduced at a strictly lower `SortOrder`.**
- **`TargetObjectId` must be unique per report** — `aliasByObjectId[rel.TargetObjectId] = alias` overwrites, so joining the same object twice yields an unaddressable alias.

- [x] **Step 1: Write the failing test** — *deviation: the cross-row rules were extracted into a pure `ReportRelationshipRules` and covered by 13 DB-free tests that actually execute, instead of a `[SkippableFact]` that skips locally without `REPORTS_TEST_DB`. The DB round-trip test (owner adds a relationship → round-trips through the list query) was **not** written and is still outstanding.*

`[SkippableFact]` mirroring the `ReportShareCommandTests` harness. Cover:
- owner adds a valid relationship → round-trips through the list query;
- `JoinType = "Full"` → `ValidationException`;
- duplicate `TargetObjectId` on the same report → `ValidationException`;
- a Source that is neither the primary object nor a lower-`SortOrder` Target → `ValidationException`.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportRelationshipCommandTests`
Expected: FAIL — commands do not exist (compile error).

- [x] **Step 3: Write commands + query + handlers**

Mirror the **Sortings** pattern exactly (`ReportCommands.cs:85-93`, `260-281`):

```csharp
public record AddReportRelationshipCommand : IRequest<ReportRelationshipDto>
{
    public Guid ReportDefinitionId { get; init; }
    public Guid SourceObjectId { get; init; }
    public Guid TargetObjectId { get; init; }
    public string JoinField { get; init; } = null!;
    public string JoinType { get; init; } = "Inner";
    public int SortOrder { get; init; }
}
public record DeleteReportRelationshipCommand(Guid Id) : IRequest;
```
```csharp
// ReportRelationshipQueries.cs
public record GetReportRelationshipsQuery(Guid ReportDefinitionId) : IRequest<List<ReportRelationshipDto>>;
```

Gate the add/delete handlers with `IReportAccessService.EnsureCanEditAsync`, and the list handler with `EnsureCanReadAsync` — matching the share handlers. (Note the existing field/filter/grouping/sorting handlers do **not** call the access service, relying on the controller permission alone; the share handlers do. Follow the **share** precedent — it is the stricter and more recent one.)

- [x] **Step 4: Write `AddReportRelationshipValidator`**

In `ReportValidators.cs`. Enforce: `JoinField` non-empty and ≤200; `JoinType` ∈ {`Inner`,`Left`,`Right`} case-insensitive; `SourceObjectId` != `TargetObjectId`.

The cross-row rules (unique target, source-ordering) need DB state, so enforce them **in the handler** with `ValidationException`, not in the FluentValidation validator.

- [x] **Step 5: Add the controller endpoints**

Mirror the Sortings endpoints:

```csharp
// Relationships (joins)
[HttpGet("{id:guid}/relationships")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<List<ReportRelationshipDto>>>> GetRelationships(Guid id, CancellationToken ct)
{ var result = await Mediator.Send(new GetReportRelationshipsQuery(id), ct); return OkResponse(result); }

[HttpPost("{id:guid}/relationships")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse<ReportRelationshipDto>>> AddRelationship(Guid id, [FromBody] AddReportRelationshipCommand command, CancellationToken ct)
{ var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

[HttpDelete("relationships/{relationshipId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse>> DeleteRelationship(Guid relationshipId, CancellationToken ct)
{ await Mediator.Send(new DeleteReportRelationshipCommand(relationshipId), ct); return OkResponse("Relationship removed"); }
```

- [x] **Step 6: Fix Clone to copy relationships**

`CloneReportCommandHandler` (`ReportCommands.cs:176-184`) copies Fields/Filters/Groupings/Sortings but **not Relationships** — so cloning a multi-object report silently produces a single-object one whose fields reference unjoined objects (which then fails at run time). Copy `Relationships` too, preserving `SortOrder`.

- [x] **Step 7: Run tests + build**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: BUILD succeeds; new tests Skipped locally (or PASS with `REPORTS_TEST_DB`).

- [x] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(reports): relationship (join) endpoints with validation + clone copies relationships"
```

---

## Task A3: Formula authoring path (text → AST)

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Reports/ReportField.cs` (add `CalculationText`)
- Create: `backend/src/HR.Infrastructure/Migrations/*_ReportFieldCalculationText.cs` (generated)
- Modify: `backend/src/HR.Modules/Platform/Commands/Reports/ReportCommands.cs` (`AddReportFieldCommandHandler`)
- Modify: `backend/src/HR.Modules/Platform/DTOs/Reports/ReportDtos.cs` (`ReportFieldDto.CalculationText`)
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs` (formula validation endpoint)
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportFieldFormulaTests.cs`

**Do NOT write a parser.** `ExpressionParser` already exists and is exactly this:
```csharp
// backend/src/HR.Domain/Engines/Finance/Expressions/ExpressionParser.cs
public static Expr Parse(string source)          // :44 — throws ExpressionException
public static string? TryValidate(string source) // :57 — null when valid, else the error message
```

**The precedent to mirror** — `RuleEngine.cs:93-98`:
```csharp
private static Expr? CompileExpression(string? astJson, string? text)
{
    if (!string.IsNullOrWhiteSpace(astJson)) return AstJson.Deserialize(astJson);
    if (!string.IsNullOrWhiteSpace(text)) return ExpressionParser.Parse(text);
    return null;
}
```
And the payroll `Rule` entity stores **both** `ExpressionText` and `ExpressionAstJson` (`StandardPayrollSeeder.cs:153-154`). Do the same: AST-only storage is not round-trippable back into an editable formula box, so the builder could never re-open a saved computed field.

**Design:** keep `CalculationExpression` as the AST-JSON column (the resolver reads it — do not change that contract). Add `CalculationText` as the authored source. On write: if `CalculationText` is supplied, `ExpressionParser.Parse` it and `AstJson.Serialize` into `CalculationExpression`. If `CalculationExpression` is supplied directly (AST JSON), accept it as-is for backward compatibility.

**Language limits to surface in the builder UI (Task B4), verified in the evaluator:**
- **No string-concat operator.** `+` is numeric-only (`ExpressionEvaluator.cs:51`). Text joining must use `concat(...)`.
- **Comparisons are numeric-only** (`:64-67`). Dates are normalized to ISO-8601 *text*, so `hireDate > '2020-01-01'` throws `"Cannot use text ... as a number."` and `ReportRowShaper` silently nulls the cell. Use `yearsBetween(...)` / `age(...)` instead.
- **`IF` is a function, not an operator, and evaluates all args eagerly** (`ExpressionEvaluator.cs:76-78`) — no short-circuit. `AND`/`OR` do short-circuit.
- Available functions: `IF`, `AND`, `OR`, `NOT`, `MIN`, `MAX`, `ROUND`, `ABS`, `FLOOR`, `CEIL`, `CLAMP`, `COALESCE`, `PERCENT` (built-ins) + `today()`, `now()`, `age(dateText)`, `yearsBetween(from,to)`, `concat(a,b,…)`, `round(value,digits)` (report-specific, `ComputedFieldEvaluator.ReportFunctions()`).
- Variables resolve **case-insensitively against the row's field codes**.

- [ ] **Step 1: Write the failing test**

DB-free where possible. Assert:
- a valid formula (`ROUND(basicSalary * 0.09, 2)`) parses and the handler stores AST JSON in `CalculationExpression` that `AstJson.Deserialize` round-trips;
- an invalid formula (`basicSalary +`) raises `ValidationException` (**not** a raw `ExpressionException` — see Step 3);
- supplying `CalculationExpression` (AST JSON) directly still works.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportFieldFormulaTests`
Expected: FAIL — `CalculationText` does not exist (compile error).

- [ ] **Step 3: Implement**

Add to `ReportField.cs`:
```csharp
    public string? CalculationText { get; set; }
```
EF config: `HasMaxLength(2000)` in `ReportConfigurations.cs` next to the existing `CalculationExpression` property config.

In `AddReportFieldCommandHandler`, before constructing the entity:
```csharp
var calcJson = request.CalculationExpression;
if (!string.IsNullOrWhiteSpace(request.CalculationText))
{
    var error = ExpressionParser.TryValidate(request.CalculationText);
    if (error is not null)
        throw new ValidationException("CalculationText", $"Invalid formula: {error}");
    calcJson = AstJson.Serialize(ExpressionParser.Parse(request.CalculationText));
}
```
**Translate `ExpressionException` into `ValidationException`** — an unhandled `ExpressionException` maps to a 500, not a 400, so a user typo would read as a server crash. `TryValidate` is the guard that keeps it a 400.

Add `CalculationText` to `AddReportFieldCommand` and to `ReportFieldDto`.

- [ ] **Step 4: Add a formula-validation endpoint**

The builder needs live validation without saving:
```csharp
[HttpPost("validate-formula")]
[RequirePermission("Platform.Reports.Edit")]
public ActionResult<ApiResponse<FormulaValidationDto>> ValidateFormula([FromBody] ValidateFormulaRequest request)
{
    var error = ExpressionParser.TryValidate(request.Formula ?? "");
    return OkResponse(new FormulaValidationDto { IsValid = error is null, Error = error });
}
```
Declare `ValidateFormulaRequest { string? Formula }` and `FormulaValidationDto { bool IsValid; string? Error }` in `ReportDtos.cs`. This is a pure function of the input — no DB, no MediatR needed.

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add ReportFieldCalculationText --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api --context ApplicationDbContext
```
Inspect `Up()`: it must add exactly one column to `engine_report_fields` and nothing else. If unrelated changes appear, prior model drift exists — stop and report.

> Note: `20260714193059_ReportOrganization` may not yet be applied to Azure Postgres (Phase 2 deferred it to deploy). That is a **deployment** concern, not a blocker here — but confirm before deploying, since this migration stacks on it.

- [ ] **Step 6: Run tests + build**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: BUILD succeeds; tests PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(reports): formula authoring path (text -> AST via ExpressionParser) + validate-formula endpoint"
```

---

## Task A4: Runtime parameters

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs` (`RunReportQuery`)
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/IReportExecutionService.cs` + `ReportExecutionService.cs`
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/IReportObjectResolver.cs` + `ReportObjectResolver.cs`
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportParameterTests.cs`

**Current state:** `ReportFilter.IsParameter` is stored and returned but `ReportObjectResolver` (line ~102) pushes every filter's stored `Value`/`ValueTo` into SQL regardless. `POST {id}/run` takes no body.

**Design:** `POST {id}/run` gains an optional body `{ "parameters": { "<fieldCode>": "<value>", ... } }`. For a filter with `IsParameter = true`, the supplied value **overrides** the stored `Value`; the stored value remains the default when no parameter is supplied. Parameter keys match `FieldCode` case-insensitively.

**Also fix while here:** filters only resolve against the **primary object** (`primary.Field(flt.FieldCode)`) — a filter on a joined object's field is **silently dropped, not an error**. With joins now creatable (A2), silent drops become much more likely. At minimum, **throw a `ValidationException` instead of dropping**, so a user gets told rather than handed wrong numbers. (Full joined-field filtering is out of scope for R1; raising the silent drop to an error is not.)

- [ ] **Step 1: Write the failing test**

Assert: a parameterized filter with a supplied value uses the supplied value; with none, falls back to the stored default; a non-parameter filter ignores a supplied value; a filter on a joined field now raises `ValidationException` rather than vanishing.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportParameterTests`
Expected: FAIL — no parameter plumbing (compile error).

- [ ] **Step 3: Implement**

Thread `IReadOnlyDictionary<string, string?>? parameters` through `RunReportQuery` → `IReportExecutionService.RunAsync` → `IReportObjectResolver.BuildModelAsync`. In the resolver's filter loop, when `flt.IsParameter` and the dictionary has a matching key, substitute the value.

`RunForExportAsync` must accept parameters too, or exports of parameterized reports silently use defaults while the on-screen view shows something else. Thread it through both.

Keep the signature change additive (optional/defaulted) so existing callers compile.

- [ ] **Step 4: Update the controller**

```csharp
[HttpPost("{id:guid}/run")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<ReportResult>>> Run(Guid id, [FromBody] RunReportRequest? request, int page = 1, int pageSize = 50, CancellationToken ct = default)
{ var result = await Mediator.Send(new RunReportQuery(id, page, pageSize, request?.Parameters), ct); return OkResponse(result); }
```
Body must stay **optional** — the existing FE and the export path call `run` with no body. Declare `RunReportRequest { Dictionary<string, string?>? Parameters }`.

Add `format`-parity to `/export` (`?format=...&p.<fieldCode>=<value>` or a POST body) **only if trivial**; otherwise note that exporting a parameterized report uses stored defaults and surface that in the viewer UI (B3).

- [ ] **Step 5: Run tests + build**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(reports): runtime parameters on run + error (not silent drop) on joined-field filters"
```

---

## Task A5: Enrich `ReportDefinitionDto`

**Files:**
- Modify: `backend/src/HR.Modules/Platform/DTOs/Reports/ReportDtos.cs`
- Modify: `backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs`
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportListProjectionTests.cs`

The list page (B2) needs folder + tag + favorite/pin state per report; the builder (B4) needs relationships. Today `ReportDefinitionDto` exposes none of them.

- [ ] **Step 1: Write the failing test**

`[SkippableFact]`: a report in a folder with a tag, favorited by the caller, projects `folderId`, `tags`, `isFavorite`, `isPinned` correctly. Assert `isFavorite` is **per-caller** (a second user sees false).

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL — properties do not exist.

- [ ] **Step 3: Implement**

Add to `ReportDefinitionDto`:
```csharp
    public Guid? FolderId { get; set; }
    public List<ReportTagDto> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public List<ReportRelationshipDto> Relationships { get; set; } = new();
```
`FolderId` and `Relationships` map straight from the entity. `Tags` needs a join through `ReportDefinitionTag`. **`IsFavorite`/`IsPinned` are per-caller** and cannot come from AutoMapper — project them in `GetReportsQueryHandler`/`GetReportByIdQueryHandler` from `ReportUserStates` filtered to `_user.UserId`, after mapping.

Watch the N+1: load the caller's `ReportUserState` rows and the tag joins for the page's report ids in **two batched queries**, then stitch — do not query per report.

- [ ] **Step 4: Fix the dead `Scope` filter**

`GetReportsQuery.Scope` is declared but **never used** in the handler — filtering by scope silently does nothing today. Either wire it (`query = query.Where(r => r.Scope == parsed)`) or delete the property. **Wire it**; B2 exposes a scope filter.

- [ ] **Step 5: Run tests + build**

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(reports): enrich ReportDefinitionDto (folder, tags, favorite/pin, relationships) + wire Scope filter"
```

**► PART A CHECKPOINT:** Backend is complete and independently deployable. Run `dotnet test backend/tests/HR.Modules.Platform.Tests`. Two migrations are now pending against Azure (`ReportOrganization` from Phase 2 + `ReportFieldCalculationText` from A3) — apply both at deploy.

---

# PART B — Frontend

## Task B1: Extend the reports API client

**Files:**
- Modify: `src/lib/api/reports.ts`

Today it has only `getReports` and `exportReport`. Every Phase 2 endpoint is unused.

- [ ] **Step 1: Run `npm install`** (node_modules is absent).

- [ ] **Step 2: Extend the client**

Add types mirroring the enriched DTOs (A5) and functions for: definition CRUD (`getReport`, `createReport`, `updateReport`, `deleteReport`, `publishReport`, `cloneReport`); field/filter/grouping/sorting add+delete; relationships (A2); `runReport(id, {page, pageSize, parameters})`; folders CRUD + `setReportFolder`; tags CRUD + assign/unassign; `toggleFavorite`, `togglePin`; `getShares`/`addShare`/`removeShare`; `validateFormula` (A3); and the catalog: `getCatalogObjects`, `getCatalogFields(code)` (`/api/platform/registry/...`) plus `getObjectDefinitions` (`/api/platform/objects`).

Follow the file's existing idioms: one-line arrow consts for simple calls, `async function` for multi-step; `export interface` mirroring DTOs inline.

> **The two-catalog join — the builder's central quirk.** `ReportDefinition.PrimaryObjectId` and `ReportField.ObjectDefinitionId` are **Guids into `ObjectDefinition`** (`/api/platform/objects`), but the rich field metadata (`isMeasure`, `isGroupable`, enum `options`, `referenceObjectCode`) only exists on the **live catalog** (`/api/platform/registry/objects/{code}`), keyed by **Code**. The builder must fetch both and **join on `Code`**. An `ObjectDefinition` row whose `Code` is absent from the live catalog is selectable but fails at run time with a `ValidationException` — filter those out of the picker rather than letting a user build a broken report.

Extend `ExportFormat` if needed, but note the backend `?format=` parses `excel|csv|txt|xml|pdf`; the FE deliberately offers only `excel|csv|pdf`. Keep it that way — `txt`/`xml` writers register in **Payroll** DI, so their availability is coupled to that module.

- [ ] **Step 3: Typecheck**

Run: `npx tsc --noEmit`
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add src/lib/api/reports.ts
git commit -m "feat(reports): frontend api client for definition CRUD, catalog, organization, run, shares"
```

---

## Task B2: List page enrichment

**Files:**
- Modify: `src/app/(dashboard)/reports/page.tsx`
- Create: `src/components/reports/report-folders-sidebar.tsx`
- Create: `src/components/reports/report-tag-filter.tsx`

Spec §8: folders sidebar (nested), view filters (All / Favorites / Recent / Pinned), tag filter, search; cards show name, owner, scope, tags; pin/favorite actions.

The current page is a 155-line raw `<table>` with export buttons. It is a **partial outlier** — raw `<table>` instead of `ui/table` primitives, `usePermission` instead of `AccessGuard`, a redundant `dir="rtl"` (root already sets it), and no pagination. **Bring it in line** with the codebase: wrap in `AccessGuard anyOf={["Platform.Reports.View"]}` + `Inner`, use `ui/table` primitives, drop the redundant `dir`, add pagination.

- [ ] **Step 1: Build the folders sidebar** — nested tree from the flat `ReportFolderDto[]` (`parentFolderId`), selecting a folder sets the `folderId` list filter. Folder CRUD gated on `has("Platform.Reports.Edit")`.
- [ ] **Step 2: Add view filters + tag filter + search** — `view` ∈ `favorites|recent|pinned` (backend `GetReportsQuery.View`), `tagId`, debounced `search` (350ms, reset to page 1 — copy `run-employees-table.tsx`), and `scope` (wired in A5).
- [ ] **Step 3: Add pin/favorite actions + tag chips** per row, using the enriched DTO (A5). Optimistic toggle, revert on error.
- [ ] **Step 4: Add pagination** — reports envelope (`pageNumber`/`totalCount`/`totalPages`), footer only when `totalCount > PAGE_SIZE`.
- [ ] **Step 5: Register nav** — `/reports` is already in `sidebar.tsx`; confirm, don't duplicate.
- [ ] **Step 6: Typecheck + commit**

```bash
git add -A
git commit -m "feat(reports): list page — folders sidebar, view/tag/scope filters, favorites, pagination"
```

---

## Task B3: Report viewer

**Files:**
- Create: `src/app/(dashboard)/reports/[id]/page.tsx`
- Create: `src/components/reports/report-result-table.tsx`
- Create: `src/components/reports/report-parameter-bar.tsx`

Spec §8: grouped table with subtotals + grand total, respects field format patterns, parameter inputs, RTL, Excel/CSV export buttons.

**The `ReportResult` contract — read this before writing the table:**
```csharp
ReportResult { string ReportCode; List<ReportColumn> Columns; List<ReportGroup> Groups;
               List<ReportRow> Rows; Dictionary<string,double> GrandTotals;
               long TotalCount; int Page; int PageSize; bool Truncated; }
ReportGroup  { string FieldCode; object? Key; string Label; List<ReportGroup> SubGroups;
               List<ReportRow> Rows; Dictionary<string,double> Aggregates; long Count; }
ReportColumn { string Code; string Label; string Type; bool IsMeasure;
               string? Aggregation; string? FormatPattern; }
```
Semantics confirmed in `ReportRowShaper.Shape`:
- **`Groups` and `Rows` are mutually exclusive.** No groupings → `Rows` filled, `Groups` empty. Groupings → `Groups` filled, top-level `Rows` **empty**. **Branch on `groups.length > 0`.**
- A `ReportGroup` has **either** `SubGroups` (non-leaf) **or** `Rows` (leaf), never both → render recursively.
- `Aggregates`/`GrandTotals` are keyed by the **measure column's `Code`**; only columns with `IsMeasure && Aggregation != null` appear.
- `ReportRow` is a plain JSON object keyed by `FieldCode`.
- **`ReportColumn.Label` is populated from `DisplayNameAr` only** (`ReportObjectResolver.cs:70`) — `DisplayNameEn` never reaches the result. Fine for this Arabic-only UI; do not build an EN fallback that can never fire.
- **`ReportColumn.Type` is a `FieldKind` name** (`Text|Number|Decimal|Currency|Percentage|Date|DateTime|Boolean|Reference|Enum|Guid`); computed fields are always `"Text"`. Use it to right-align numerics + `tabular-nums`.

**Two behaviors to surface honestly in the UI, not paper over:**
- **`Truncated: true` means the report exceeded the 5000-row cap** (`RowCap`); paging is in-memory over that capped set, not SQL `OFFSET`. **Show a visible truncation banner** — silently paging a truncated set reads as complete data.
- **When grouped, paging does not slice.** `Page`/`PageSize` are echoed back but all groups return, and `TotalCount` is total rows, not groups. **Hide the pager when `groups.length > 0`** rather than rendering a pager that does nothing.

- [ ] **Step 1: Page shell** — `AccessGuard anyOf={["Platform.Reports.View"]}`, Next-16 async params (`params: Promise<{id}>` + `use(params)`), breadcrumb with `ArrowRight`, header with name + scope/published badges, export buttons gated on `has("Platform.Reports.Export")`, edit button on `Platform.Reports.Edit` → `/reports/[id]/edit`.
- [ ] **Step 2: Parameter bar** — inputs for each filter with `isParameter: true`, typed by the column's `FieldKind`; "Run" applies. If A4's export-parameter parity was skipped, note in the UI that export uses stored defaults.
- [ ] **Step 3: Flat table** — `ui/table` primitives, format by `FormatPattern`/`Type`, `tabular-nums`, hand-rolled pagination (reports envelope), grand-total footer row.
- [ ] **Step 4: Grouped rendering** — recursive group component, indented headers showing `Label` + `Count`, subtotal row per group from `Aggregates`, grand total at the bottom. Collapsible groups.
- [ ] **Step 5: Truncation banner** when `truncated`.
- [ ] **Step 6: Typecheck + commit**

```bash
git add -A
git commit -m "feat(reports): report viewer — grouped table, subtotals, grand totals, parameters, export"
```

---

## Task B4: Builder wizard

**Files:**
- Create: `src/app/(dashboard)/reports/new/page.tsx`
- Create: `src/app/(dashboard)/reports/[id]/edit/page.tsx`
- Create: `src/components/reports/builder/report-builder.tsx` (+ step components)

Spec §8: linear vertical wizard — primary object → joins → fields → filters → grouping & sorting → save + share.

**Copy `ApprovalWorkflowWizard`'s idioms** (`src/components/workflows/wizard/ApprovalWorkflowWizard.tsx`) — there is nothing to import:
- step state as a numeric `useState` (**generalize its `1|2` to a step index — we have 6 steps**);
- a **private local `Stepper`** component at the bottom of the file;
- footer nav with inline conditionals; `ChevronRight` = back, `ChevronLeft` = next;
- `useMemo`-derived per-step error arrays gating the next/save button;
- `Promise.allSettled` + `status === "fulfilled"` guards for parallel lookup loading, so one failed dropdown doesn't kill the wizard;
- **raw `useState`, not react-hook-form/zod** — both are in `package.json` but neither wizard uses them. Follow the wizards, not the dependency list.

`ConditionEditor` (`ApprovalWorkflowWizard.tsx:317`) is a field/operator/value row builder — **the filters step is nearly the same UI**. Adapt it rather than starting cold.

**Persistence reality: there is no batch save.** Fields/filters/groupings/sortings/relationships are **add + delete only — there is no update endpoint**. Editing an existing report means delete-then-re-add. So:
- **New report:** `createReport` first (needs `code`, `nameEn`, `nameAr`, `reportType`, `scope`, `primaryObjectId`), then add children against the returned id. `Code` and `PrimaryObjectId` are **immutable after create** (`UpdateReportCommand` excludes them) — the wizard must not offer to change them in edit mode.
- **Edit report:** diff the wizard state against the loaded definition and issue add/delete calls per child. Do not attempt PUT on children.

- [ ] **Step 1: Step 1 — primary object.** Fetch `/api/platform/objects` (Guids) **and** `/api/platform/registry/objects` (metadata), **join on `Code`**, and offer only objects present in both (see B1). Plus name/code/type/scope inputs. Code immutable in edit mode.
- [ ] **Step 2: Step 2 — joins.** Suggest candidates from the catalog's `isReference` + `referenceObjectCode` fields (there is **no relationship list** on `CatalogObjectDto` — joins are inferred from FK fields). Remember `JoinField` is a field on the **Source**; the join is always FK→PK. Enforce the `SortOrder` ordering rule client-side (source must be the primary object or an already-added target) so the user gets an inline error, not a 400.
- [ ] **Step 3: Step 3 — fields.** Object fields (from the joined objects), aggregates (`AggregationType`), and the **computed formula input** wired to `validateFormula` (A3) with live inline errors. Surface the language limits in helper text: `+` is numeric-only (use `concat`), comparisons are numeric-only (use `yearsBetween`/`age` for dates), `IF` is a function. **Field codes must be globally unique within a report** — the resolver rejects duplicates; validate client-side.
- [ ] **Step 4: Step 4 — filters.** Adapt `ConditionEditor`. Expose `isParameter`. **Note in the UI that `LogicalOperator` is not honored** — the SQL builder cannot express OR/grouping; all filters are ANDed. Either hide the control or show it disabled with an explanation; do **not** offer an OR that silently ANDs. Filters resolve against the **primary object only** (A4 makes joined-field filters a hard error) — mark joined fields unselectable here.
- [ ] **Step 5: Step 5 — grouping & sorting.** Groupable fields from catalog `isGroupable`. Note that grouping disables paging (B3).
- [ ] **Step 6: Step 6 — save + share.** Persist per the add/delete reality above; optional shares via the Phase 2 endpoints (`AddReportShareCommand` param order is **User, Role, Department** — the DTO declares User, Department, Role; JSON binds by name, but don't mirror DTO order into the command). Redirect to the viewer.
- [ ] **Step 7: Typecheck + build**

Run: `npx tsc --noEmit && npm run build`

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(reports): report builder wizard (object -> joins -> fields -> filters -> grouping -> save)"
```

---

## Verification

- [ ] `dotnet test backend/tests/HR.Modules.Platform.Tests` — all green.
- [ ] `npx tsc --noEmit && npm run build` — clean.
- [ ] Drive the real flow via `/run` or the verify skill: build a two-object report with a join, a computed field, a group + aggregate, and a parameterized filter → run it → confirm grouped subtotals and grand total → export Excel/CSV/PDF → favorite + file it in a folder → confirm it appears under the Favorites view filter.
- [ ] **Tenant isolation (A0):** confirm a joined tenant-scoped object cannot return another tenant's rows. This is the security fix — verify it explicitly, do not infer it from a green unit test.

## Deployment notes

- **Two migrations pending against Azure Postgres:** `20260714193059_ReportOrganization` (Phase 2, deferred at the time) and `ReportFieldCalculationText` (A3). Confirm the Phase 2 one is actually applied before stacking A3 on it. Password: Key Vault `secretpulse/hrcloud-db-password`.
- API: `hrcloud-api-v4xd` (West Europe). Zip-deploy gotcha: build the zip via `System.IO.Compression.ZipFile` with `.Replace('\\','/')` on entry names — PowerShell `Compress-Archive` writes `\` paths that Linux Kudu rsync rejects.

## Out of scope (deferred past R1)

- **R3** — generic report→SIF/WPS `IExportWriter`.
- **R4** — `IReportExecutionScheduler` + `ReportSchedule` delivery. (Schedule endpoints/entities exist; nothing runs them.)
- **R2 streaming** — the PDF writer landed early, but stream-based generation (which would remove the 5000-row export cap) did not. Export remains row-capped.
- **Joined-field filters** — A4 turns the silent drop into an error; actually filtering on a joined object's field stays deferred.
- **`ExportFormat` enum collision** — two different enums share the name: `HR.Domain.Enums.ExportFormat` (`Pdf=1, Xlsx=2, Csv=3, Png=4`, bound by `AddReportScheduleCommand`) vs `HR.Application.Engines.Finance.Export.ExportFormat` (`Excel=1, Csv=2, Txt=3, Xml=4, Pdf=5`, parsed by `/export`). Different members *and* different numeric values. Harmless while schedules don't execute; **a live hazard the moment R4 ships** (a schedule stored as `Pdf=1` would parse as `Excel=1` through an int round-trip). Fix with R4.
