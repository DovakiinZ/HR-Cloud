# Payroll Sub-project 3 — Run Details + Quick Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Payroll Run Details experience (server-aggregated KPIs, paginated employees/excluded/validation/transactions/calculations, lifecycle+calculation timeline, run-page create-from-run quick actions) and fully close orphan-transaction bug #4, preserving the immutable-ledger / read-only-consume / run-state-machine architecture.

**Architecture:** All changes additive. Run gains a stored, immutable `(TargetPeriodYear, TargetPeriodMonth)` identity; `PayrollPeriodResolver` stays the only membership authority. Bug #4 is sealed on both doors: a create-side guard in `PayrollTransactionService` at the becomes-Approved boundary, plus a derived staleness gate on the run lifecycle. Each Calculate writes an append-only, monotonically-versioned `PayrollRunCalculation` snapshot (metadata + findings + exclusions + change-summary). The read model is decomposed into paginated sub-resources.

**Tech Stack:** C# / .NET 8, EF Core 8 (Npgsql/PostgreSQL), MediatR, xUnit + FluentAssertions (existing test style), ASP.NET Core controllers with `[RequirePermission]`; Next.js 16 / React 19 / TS / Tailwind / shadcn for the run page.

**Spec:** `docs/superpowers/specs/2026-07-03-payroll-subproject-3-run-details-design.md` (decisions D1–D10).

## Global Constraints

- **Immutability:** never mutate ledger entries; consume stays read-only (no `PayrollRunId` stamping at Calculate — only at Execute). No run state changes spontaneously.
- **Resolver-canonical:** membership = `PayrollPeriodResolver.Resolve(EffectiveDate, cutoffDay, carryToNextPeriod)` only. Never use `PayrollTransaction.TargetPeriodYear/Month` for business logic. Never derive a run's period from `PeriodStart/End`.
- **Multi-tenant:** every new query is tenant-scoped (global query filters apply automatically via `ApplicationDbContext`).
- **UTC:** persist `DateTime` as UTC (`AsUtc` pattern already in `PayrollTransactionService`).
- **Business-rule failures throw `DomainException` → HTTP 422** (existing 2C pattern via `ExceptionHandlingMiddleware`).
- **Guard lives ONLY in `PayrollTransactionService`** at the becomes-Approved boundary. No controller/UI does its own period validation.
- **`Origin` is non-nullable** on `PayrollTransaction`. `SourceModule` (business system) and `Origin` (UI/API) are separate, never merged.
- **Enums keyed by business meaning** in `HR.Domain`, not master data.
- **TDD:** failing test first for every domain/infra behavior. Tests in `backend/tests/HR.Domain.Finance.Tests` unless noted. Run from `backend/`.
- **Commit** after each task with the shown message.

**Test run command (all backend):** `dotnet test backend/tests/HR.Domain.Finance.Tests/HR.Domain.Finance.Tests.csproj`
**Build:** `dotnet build backend/src/HR.Api/HR.Api.csproj`

---

## Phase 1 — Domain types & run period identity

### Task 1: New domain enums + `ValidationSeverity.Information`

**Files:**
- Create: `backend/src/HR.Domain/Enums/PayrollRunDetailsEnums.cs`
- Modify: the existing severity enum (find with grep; see Step 1) to add `Information`
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollRunDetailsEnumsTests.cs`

**Interfaces:**
- Produces: `enum PayrollExclusionReasonCode { ExcludedByScope=1, NotEmployedInPeriod=2, NoActiveSalary=3, AlreadyInActiveRunForPeriod=4 }`; `enum PayrollTransactionOrigin { System=0, RunPage=1, AttendanceDaily=2, DeductionsPage=3, AdditionsPage=4, Import=5, API=6, Migration=7, Workflow=8, ESS=9, Scheduler=10 }`; `enum PayrollCalculationTriggerSource { Manual=1, Recalculate=2, Auto=3 }`; `ValidationSeverity.Information`.

- [ ] **Step 1: Locate the existing severity type.** Run: `grep -rn "enum.*Severity\|Severity {" backend/src/HR.Domain backend/src/HR.Application` and open the finance validation finding/severity type (it currently has at least `Error`, `Warning`). Note its namespace + name for the test.

- [ ] **Step 2: Write the failing test**
```csharp
using FluentAssertions;
using HR.Domain.Enums;
using Xunit;

public class PayrollRunDetailsEnumsTests
{
    [Fact]
    public void Origin_System_is_default_zero()
        => ((int)PayrollTransactionOrigin.System).Should().Be(0);

    [Fact]
    public void Exclusion_reasons_are_stable_values()
    {
        ((int)PayrollExclusionReasonCode.ExcludedByScope).Should().Be(1);
        ((int)PayrollExclusionReasonCode.AlreadyInActiveRunForPeriod).Should().Be(4);
    }

    [Fact]
    public void Reserved_origins_exist()
        => System.Enum.GetNames<PayrollTransactionOrigin>()
            .Should().Contain(new[] { "RunPage", "API", "Migration", "Workflow", "ESS", "Scheduler" });
}
```

- [ ] **Step 3: Run test to verify it fails.** Run: `dotnet test backend/tests/HR.Domain.Finance.Tests/HR.Domain.Finance.Tests.csproj --filter PayrollRunDetailsEnumsTests`. Expected: FAIL (types not defined).

- [ ] **Step 4: Create the enums file**
```csharp
namespace HR.Domain.Enums;

public enum PayrollExclusionReasonCode
{
    ExcludedByScope = 1,
    NotEmployedInPeriod = 2,
    NoActiveSalary = 3,
    AlreadyInActiveRunForPeriod = 4,
}

// Origin = the UI/API surface that created the transaction (distinct from SourceModule,
// which is the business system). Values 5..10 are reserved now to avoid later redesign.
public enum PayrollTransactionOrigin
{
    System = 0,
    RunPage = 1,
    AttendanceDaily = 2,
    DeductionsPage = 3,
    AdditionsPage = 4,
    Import = 5,
    API = 6,
    Migration = 7,
    Workflow = 8,
    ESS = 9,
    Scheduler = 10,
}

public enum PayrollCalculationTriggerSource { Manual = 1, Recalculate = 2, Auto = 3 }
```

- [ ] **Step 5: Add `Information` to the severity enum** found in Step 1 (append `, Information` to the enum members). If severity is a string today, skip and record that finding — Task 11 will formalize it.

- [ ] **Step 6: Run test to verify it passes.** Expected: PASS.

- [ ] **Step 7: Commit**
```bash
git add backend/src/HR.Domain/Enums/PayrollRunDetailsEnums.cs backend/tests/HR.Domain.Finance.Tests/PayrollRunDetailsEnumsTests.cs
git commit -m "feat(payroll-3): domain enums for exclusions, transaction origin, calc trigger"
```

---

### Task 2: `PayrollRun` period identity + calc pointers (columns, config, migration, backfill, unique index)

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Finance/Entities/PayrollRun.cs`
- Modify: the run's EF config in `backend/src/HR.Infrastructure/Persistence/Configurations/Engines/FinanceConfigurations.cs` (grep to confirm file)
- Modify: `backend/src/HR.Infrastructure/Engines/Finance/PayrollRunEngine.cs` (`CreateAsync` — stamp year/month)
- Migration: `backend/src/HR.Infrastructure/Migrations/` (generated)
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollRunPeriodIdentityTests.cs`

**Interfaces:**
- Produces on `PayrollRun`: `int TargetPeriodYear`, `int TargetPeriodMonth`, `int CurrentCalculationVersion` (default 0), `DateTime? LastCalculatedAt`, `Guid? LastCalculatedByUserId`. `CreateAsync` stamps `TargetPeriodYear/Month` from the requested period and they are never reassigned afterward.

- [ ] **Step 1: Write the failing test** (uses the existing in-memory/SQLite test harness — grep `HR.Domain.Finance.Tests` for how a test `ApplicationDbContext` is built, e.g. `PayrollTransactionPersistenceTests`, and reuse that helper):
```csharp
[Fact]
public async Task CreateAsync_stamps_target_period_from_request()
{
    await using var ctx = TestDb.Create();            // reuse existing harness helper
    var engine = TestFactory.RunEngine(ctx);          // reuse existing engine factory in tests
    var defId = await TestSeed.PublishedMonthlyDefinition(ctx);

    var run = await engine.CreateAsync(defId, PayrollPeriod.Monthly(2026, 7), default);

    run.TargetPeriodYear.Should().Be(2026);
    run.TargetPeriodMonth.Should().Be(7);
}
```
> If no `TestDb`/`TestFactory`/`TestSeed` helpers exist, grep an existing engine test (e.g. `AttendanceDeductionRunTests`) and copy its setup verbatim into this test's arrange block.

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL (no `TargetPeriodYear`).

- [ ] **Step 3: Add columns to `PayrollRun.cs`**
```csharp
public int TargetPeriodYear { get; set; }
public int TargetPeriodMonth { get; set; }
public int CurrentCalculationVersion { get; set; }   // 0 until first Calculate
public DateTime? LastCalculatedAt { get; set; }
public Guid? LastCalculatedByUserId { get; set; }
```

- [ ] **Step 4: Stamp them in `PayrollRunEngine.CreateAsync`.** Locate where the `PayrollRun` is constructed (it already receives `PayrollPeriod period`). Add:
```csharp
TargetPeriodYear = period.Year,
TargetPeriodMonth = period.Month,
```
(Do not set them anywhere else — they are immutable after creation.)

- [ ] **Step 5: Configure columns + index in `FinanceConfigurations`.** Add to the `PayrollRun` builder:
```csharp
builder.Property(x => x.TargetPeriodYear).IsRequired();
builder.Property(x => x.TargetPeriodMonth).IsRequired();
builder.HasIndex(x => new { x.TargetPeriodYear, x.TargetPeriodMonth });
// One active run per (definition, period). Excludes Cancelled now; SP6 will extend the filter
// to also exclude Voided/Superseded.
builder.HasIndex(x => new { x.PayrollDefinitionId, x.TargetPeriodYear, x.TargetPeriodMonth })
       .IsUnique()
       .HasFilter("\"State\" <> 11");   // 11 = Cancelled (PayrollRunState.Cancelled)
```

- [ ] **Step 6: Generate the migration + backfill.** Run:
```bash
dotnet ef migrations add PayrollRunTargetPeriodAndCalcPointers \
  --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api
```
Then edit the generated `Up()` to backfill existing rows **before** the unique index is created (EF orders AddColumn then CreateIndex; insert the SQL between them, or add at the end of AddColumn block):
```csharp
migrationBuilder.Sql(@"
    UPDATE engine_payroll_runs
    SET ""TargetPeriodYear"" = EXTRACT(YEAR FROM ""PeriodStart"")::int,
        ""TargetPeriodMonth"" = EXTRACT(MONTH FROM ""PeriodStart"")::int
    WHERE ""TargetPeriodYear"" = 0;");
```
> Confirm the run table name via the config/migration (`engine_payroll_runs`). If EF creates the unique index before this backfill runs and existing duplicate (defId, 0, 0) rows collide, move the `Sql` backfill to the top of `Up()`.

- [ ] **Step 7: Add the immutability regression test**
```csharp
[Fact]
public async Task Target_period_is_not_reassigned_by_calculate()
{
    await using var ctx = TestDb.Create();
    var engine = TestFactory.RunEngine(ctx);
    var defId = await TestSeed.PublishedMonthlyDefinition(ctx);
    var run = await engine.CreateAsync(defId, PayrollPeriod.Monthly(2026, 7), default);
    await engine.CalculateAsync(run.Id, default);
    var reloaded = await ctx.PayrollRuns.FindAsync(run.Id);
    (reloaded!.TargetPeriodYear, reloaded.TargetPeriodMonth).Should().Be((2026, 7));
}
```

- [ ] **Step 8: Run tests.** Expected: PASS.

- [ ] **Step 9: Commit**
```bash
git add backend/src/HR.Domain backend/src/HR.Infrastructure backend/tests
git commit -m "feat(payroll-3): stored immutable run TargetPeriodYear/Month + calc pointers + unique active-run index"
```

---

## Phase 2 — Bug #4 create-side guard (D2)

### Task 3: `PayrollPeriodClosedException` + structured 422 payload

**Files:**
- Create: `backend/src/HR.Application/Engines/Finance/PayrollPeriodClosedException.cs`
- Modify: `backend/src/HR.Api/Middleware/ExceptionHandlingMiddleware.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollPeriodClosedExceptionTests.cs`

**Interfaces:**
- Produces: `sealed class PayrollPeriodClosedException : DomainException` with `public PayrollPeriodClosedPayload Payload { get; }`; record `PayrollPeriodClosedPayload(string ErrorCode, Guid BlockingRunId, string BlockingRunNumber, Guid PayrollDefinitionId, int TargetPeriodYear, int TargetPeriodMonth, string BlockingRunState)`; `ErrorCode == "PAYROLL_PERIOD_CLOSED"`.

- [ ] **Step 1: Read** `backend/src/HR.Application/...` for the existing `DomainException` (grep `class DomainException`) and `ExceptionHandlingMiddleware.cs` to see how `DomainException` currently maps to 422 and how the error body is shaped.

- [ ] **Step 2: Write the failing test**
```csharp
[Fact]
public void Exception_carries_structured_payload()
{
    var ex = new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
        "PAYROLL_PERIOD_CLOSED", System.Guid.NewGuid(), "PR-2026-00007",
        System.Guid.NewGuid(), 2026, 7, "Approved"));
    ex.Payload.ErrorCode.Should().Be("PAYROLL_PERIOD_CLOSED");
    ex.Payload.TargetPeriodMonth.Should().Be(7);
    ex.Should().BeAssignableTo<DomainException>();
}
```

- [ ] **Step 3: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 4: Create the exception**
```csharp
namespace HR.Application.Engines.Finance;

public sealed record PayrollPeriodClosedPayload(
    string ErrorCode, System.Guid BlockingRunId, string BlockingRunNumber,
    System.Guid PayrollDefinitionId, int TargetPeriodYear, int TargetPeriodMonth, string BlockingRunState);

public sealed class PayrollPeriodClosedException : DomainException   // adjust base namespace to match Step 1
{
    public PayrollPeriodClosedPayload Payload { get; }
    public PayrollPeriodClosedException(PayrollPeriodClosedPayload payload)
        : base($"Payroll period {payload.TargetPeriodYear}-{payload.TargetPeriodMonth:D2} is closed by run {payload.BlockingRunNumber} ({payload.BlockingRunState}).")
        => Payload = payload;
}
```

- [ ] **Step 5: Map it in `ExceptionHandlingMiddleware`.** In the branch that handles `DomainException` (returns 422), add a check first: if the exception is `PayrollPeriodClosedException`, serialize a body that includes the `Payload` object (e.g. `{ success=false, message=ex.Message, errorCode=payload.ErrorCode, data=payload }`) with status 422. Keep the generic `DomainException` → 422 behavior unchanged for others.

- [ ] **Step 6: Run test + build.** `dotnet build backend/src/HR.Api/HR.Api.csproj`. Expected: PASS + build OK.

- [ ] **Step 7: Commit**
```bash
git add backend/src/HR.Application backend/src/HR.Api backend/tests
git commit -m "feat(payroll-3): PayrollPeriodClosedException with structured 422 payload"
```

---

### Task 4: Period guard service — find the immutable run for (employee, effectiveDate)

**Files:**
- Create: `backend/src/HR.Infrastructure/Engines/Finance/PayrollPeriodGuard.cs`
- Create interface: `backend/src/HR.Application/Engines/Finance/IPayrollPeriodGuard.cs`
- Modify: `backend/src/HR.Infrastructure/DependencyInjection.cs` (register)
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollPeriodGuardTests.cs`

**Interfaces:**
- Produces: `interface IPayrollPeriodGuard { Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct); }` — throws `PayrollPeriodClosedException` if an `IsImmutable` run covers the employee+resolved period; returns normally otherwise.

- [ ] **Step 1: Read** `PayrollPeriodResolver` (grep `class PayrollPeriodResolver`) for the exact `Resolve` signature/return type, and `PayrollRunStateMachine.IsImmutable`. Read `PayrollRunPopulation` for the membership fields (`PayrollRunId`, `EmployeeId`, `IsIncluded`). Read how a run's pinned version cutoff is loaded (join `PayrollDefinitionVersion` by `run.PayrollDefinitionVersionId`; fields `CutoffDay`, `CarryToNextPeriod`).

- [ ] **Step 2: Write the failing test**
```csharp
[Fact]
public async Task Blocks_when_an_immutable_run_covers_the_period()
{
    await using var ctx = TestDb.Create();
    var (defId, emp, run) = await TestSeed.ApprovedRunWithEmployee(ctx, 2026, 7); // Approved => immutable
    var guard = new PayrollPeriodGuard(ctx);
    var effective = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    var act = () => guard.EnsurePeriodOpenForAsync(emp, effective, default);

    (await act.Should().ThrowAsync<PayrollPeriodClosedException>())
        .Which.Payload.BlockingRunState.Should().Be("Approved");
}

[Fact]
public async Task Allows_when_run_is_still_mutable()
{
    await using var ctx = TestDb.Create();
    var (defId, emp, run) = await TestSeed.DraftRunWithEmployee(ctx, 2026, 7); // Draft => mutable
    var guard = new PayrollPeriodGuard(ctx);
    var act = () => guard.EnsurePeriodOpenForAsync(emp, new DateTime(2026,7,15,0,0,0,DateTimeKind.Utc), default);
    await act.Should().NotThrowAsync();
}
```
> Add `TestSeed.ApprovedRunWithEmployee` / `DraftRunWithEmployee` helpers if absent: seed a published monthly definition (CutoffDay e.g. 27, CarryToNextPeriod false), a run for (year, month) in the given state, one `PayrollRunPopulation` row (IsIncluded=true) for the employee.

- [ ] **Step 3: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 4: Implement the guard**
```csharp
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;                 // PayrollPeriodResolver
using HR.Domain.Engines.Finance.StateMachine;    // PayrollRunStateMachine
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

public sealed class PayrollPeriodGuard : IPayrollPeriodGuard
{
    private readonly ApplicationDbContext _db;
    public PayrollPeriodGuard(ApplicationDbContext db) => _db = db;

    public async Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct)
    {
        // Candidate runs: this employee is in an included population row, run state is immutable.
        var candidates = await (
            from pop in _db.PayrollRunPopulations
            where pop.EmployeeId == employeeId && pop.IsIncluded
            join run in _db.PayrollRuns on pop.PayrollRunId equals run.Id
            join ver in _db.PayrollDefinitionVersions on run.PayrollDefinitionVersionId equals ver.Id
            select new { run.Id, run.RunNumber, run.State, run.PayrollDefinitionId,
                         run.TargetPeriodYear, run.TargetPeriodMonth, ver.CutoffDay, ver.CarryToNextPeriod })
            .ToListAsync(ct);

        foreach (var c in candidates)
        {
            if (!PayrollRunStateMachine.IsImmutable(c.State)) continue;
            var (year, month) = PayrollPeriodResolver.Resolve(effectiveDate, c.CutoffDay, c.CarryToNextPeriod);
            if (year == c.TargetPeriodYear && month == c.TargetPeriodMonth)
                throw new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
                    "PAYROLL_PERIOD_CLOSED", c.Id, c.RunNumber, c.PayrollDefinitionId,
                    c.TargetPeriodYear, c.TargetPeriodMonth, c.State.ToString()));
        }
    }
}
```
> Adjust `PayrollPeriodResolver.Resolve` destructuring to its real return shape (tuple vs a `PayrollPeriod`), and DbSet names (`PayrollRunPopulations`, `PayrollDefinitionVersions`) to the actual `ApplicationDbContext` properties.

- [ ] **Step 5: Register in `DependencyInjection.cs`** (Finance section):
```csharp
services.AddScoped<HR.Application.Engines.Finance.IPayrollPeriodGuard, HR.Infrastructure.Engines.Finance.PayrollPeriodGuard>();
```

- [ ] **Step 6: Run tests.** Expected: PASS.

- [ ] **Step 7: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): PayrollPeriodGuard resolves immutable-run membership via the period resolver"
```

---

### Task 5: Wire the guard into `PayrollTransactionService` at the becomes-Approved boundary

**Files:**
- Modify: `backend/src/HR.Infrastructure/Engines/Finance/PayrollTransactionService.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollTransactionGuardTests.cs`

**Interfaces:**
- Consumes: `IPayrollPeriodGuard.EnsurePeriodOpenForAsync`.
- Produces: `ApproveAsync` and any born-Approved create path throw `PayrollPeriodClosedException` when the target period is immutable. Constructor gains `IPayrollPeriodGuard`.

- [ ] **Step 1: Write the failing test**
```csharp
[Fact]
public async Task ApproveAsync_blocks_when_period_is_closed()
{
    await using var ctx = TestDb.Create();
    var (defId, emp, run) = await TestSeed.ApprovedRunWithEmployee(ctx, 2026, 7);
    var svc = TestFactory.TransactionService(ctx);   // includes a real PayrollPeriodGuard(ctx)
    var typeId = await TestSeed.DeductionType(ctx, "MANUAL");
    var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
        PayrollTransactionKind.Deduction, emp, typeId, 100m,
        new DateTime(2026,7,15,0,0,0,DateTimeKind.Utc), null, false, null, null, null, SubmitImmediately: true), default);

    var act = () => svc.ApproveAsync(id, default);

    await act.Should().ThrowAsync<PayrollPeriodClosedException>();
}
```
> `CreatePayrollTransactionArgs` positional args must match the current record (see Task 9 for its expanded shape; for now match the existing constructor).

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL (approve currently succeeds).

- [ ] **Step 3: Inject the guard + call it at the becomes-Approved boundary.** Modify constructor:
```csharp
private readonly IPayrollPeriodGuard _guard;
public PayrollTransactionService(ApplicationDbContext db, ICurrentUserService user, IPayrollPeriodGuard guard)
{ _db = db; _user = user; _guard = guard; }
```
In `TransitionAsync`, before persisting a transition **to Approved**, call the guard:
```csharp
private async Task TransitionAsync(Guid id, PayrollTransactionStatus to, string? reason, CancellationToken ct)
{
    var txn = await GetTrackedAsync(id, ct);
    PayrollTransactionStateMachine.EnsureCanTransition(txn.Status, to);
    if (to == PayrollTransactionStatus.Approved)
        await _guard.EnsurePeriodOpenForAsync(txn.EmployeeId, txn.EffectiveDate, ct);
    txn.Status = to;
    if (reason is not null) txn.StatusReason = reason;
    await _db.SaveChangesAsync(ct);
}
```
> `CreateAsync` currently never produces `Approved` directly, so no change there yet. Task 6 adds a guarded born-Approved path used by attendance sync.

- [ ] **Step 4: Run test to verify it passes.** Expected: PASS. Also add an "allows on mutable run" test mirroring Task 4.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): guard payroll transactions at the becomes-Approved boundary (bug #4 door 1)"
```

---

### Task 6: Route attendance sync through the shared guard

**Files:**
- Modify: `backend/src/HR.Infrastructure/Engines/Finance/AttendancePayrollSyncService.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendanceSyncGuardTests.cs`

**Interfaces:**
- Consumes: `IPayrollPeriodGuard`.
- Produces: attendance materialization that would create/keep a born-`Approved` transaction calls `EnsurePeriodOpenForAsync` first; blocked against immutable periods, allowed during Calculate (run mutable).

- [ ] **Step 1: Read** `AttendancePayrollSyncService.cs` to find where it sets a transaction to `Approved` / born-Approved (the idempotent upsert from 2D).

- [ ] **Step 2: Write the failing test**
```csharp
[Fact]
public async Task Sync_now_blocks_when_period_closed()
{
    await using var ctx = TestDb.Create();
    var (version, emp) = await TestSeed.ApprovedRunWithAttendancePenalty(ctx, 2026, 7);
    var sync = TestFactory.AttendanceSync(ctx);      // wired with PayrollPeriodGuard(ctx)
    var act = () => sync.SyncAsync(version, PayrollPeriod.Monthly(2026, 7), new[] { emp }, default);
    await act.Should().ThrowAsync<PayrollPeriodClosedException>();
}
```

- [ ] **Step 3: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 4: Inject `IPayrollPeriodGuard`** into `AttendancePayrollSyncService` and, immediately before creating a new born-Approved transaction or flipping one to Approved for `(employee, period)`, call `await _guard.EnsurePeriodOpenForAsync(employeeId, effectiveDate, ct)`. Use the same `EffectiveDate` the sync assigns (a date inside the period). During `PayrollRunEngine.CalculateAsync` the run is still `Draft/Preview`, so the guard passes.

- [ ] **Step 5: Run tests** including an existing 2D sync test to confirm no regression. Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): route attendance sync through the shared period guard (no path bypasses it)"
```

---

## Phase 3 — Bug #4 lifecycle staleness gate (D3)

### Task 7: Derived staleness evaluator

**Files:**
- Create: `backend/src/HR.Infrastructure/Engines/Finance/PayrollRunStalenessEvaluator.cs`
- Create interface: `backend/src/HR.Application/Engines/Finance/IPayrollRunStalenessEvaluator.cs`
- Modify: `DependencyInjection.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollRunStalenessTests.cs`

**Interfaces:**
- Produces: `interface IPayrollRunStalenessEvaluator { Task<bool> IsStaleAsync(Guid runId, CancellationToken ct); }` — true when an `Approved`/in-period/in-population transaction is not in the current payslip snapshot, OR a snapshot `TXN:` line is now `Reversed`.

- [ ] **Step 1: Read** how the consumer selects consumable transactions (`PayrollTransactionConsumer.GetConsumableAsync`) and how payslip `ComponentsJson` stores `TXN:{id:N}` lines (grep `"TXN:"` in `PayrollTransactionMerge` / `PayslipLedgerMapper`).

- [ ] **Step 2: Write the failing test**
```csharp
[Fact]
public async Task Run_is_stale_when_approved_txn_not_in_snapshot()
{
    await using var ctx = TestDb.Create();
    var (defId, emp, run) = await TestSeed.CalculatedRunWithEmployee(ctx, 2026, 7); // Preview, snapshot built
    await TestSeed.ApprovedManualDeduction(ctx, emp, 2026, 7, amount: 50m);          // approved AFTER calc
    var evalr = new PayrollRunStalenessEvaluator(ctx, TestFactory.Consumer(ctx));
    (await evalr.IsStaleAsync(run.Id, default)).Should().BeTrue();
}

[Fact]
public async Task Run_is_not_stale_right_after_calculate()
{
    await using var ctx = TestDb.Create();
    var (defId, emp, run) = await TestSeed.CalculatedRunWithEmployee(ctx, 2026, 7);
    var evalr = new PayrollRunStalenessEvaluator(ctx, TestFactory.Consumer(ctx));
    (await evalr.IsStaleAsync(run.Id, default)).Should().BeFalse();
}
```

- [ ] **Step 3: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 4: Implement**
```csharp
public sealed class PayrollRunStalenessEvaluator : IPayrollRunStalenessEvaluator
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollTransactionConsumer _consumer;
    public PayrollRunStalenessEvaluator(ApplicationDbContext db, IPayrollTransactionConsumer consumer)
    { _db = db; _consumer = consumer; }

    public async Task<bool> IsStaleAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Run {runId} not found.");
        var ver = await _db.PayrollDefinitionVersions.FirstAsync(v => v.Id == run.PayrollDefinitionVersionId, ct);

        var empIds = await _db.PayrollRunPopulations
            .Where(p => p.PayrollRunId == runId && p.IsIncluded).Select(p => p.EmployeeId).ToListAsync(ct);

        var consumable = await _consumer.GetConsumableAsync(
            run.TargetPeriodYear, run.TargetPeriodMonth, empIds, ver.CutoffDay, ver.CarryToNextPeriod, ct);
        var consumableIds = consumable.Select(c => c.TransactionId).ToHashSet();

        // TXN ids currently reflected in the payslip snapshot.
        var snapshotTxnIds = (await _db.PayrollPayslips.Where(p => p.PayrollRunId == runId)
            .Select(p => p.ComponentsJson).ToListAsync(ct))
            .SelectMany(ParseTxnIds).ToHashSet();

        // Stale if a consumable txn isn't in the snapshot ...
        if (consumableIds.Except(snapshotTxnIds).Any()) return true;
        // ... or a snapshot txn is now Reversed (no longer consumable/valid).
        if (snapshotTxnIds.Except(consumableIds).Any()) return true;
        return false;
    }

    private static IEnumerable<Guid> ParseTxnIds(string? componentsJson) { /* parse "TXN:{id:N}" codes; see PayrollTransactionMerge */ yield break; }
}
```
> Implement `ParseTxnIds` to match the exact `TXN:` code format used by `PayrollTransactionMerge` (Step 1). Register the service in DI.

- [ ] **Step 5: Run tests.** Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): derived run staleness evaluator (snapshot vs consumable set)"
```

---

### Task 8: Staleness gate on Validate/Submit/Approve + Recalculate resets to Preview

**Files:**
- Modify: `backend/src/HR.Infrastructure/Engines/Finance/PayrollRunEngine.cs`
- Modify: `backend/src/HR.Application/Engines/Finance/IPayrollRunEngine.cs` (if `RecalculateAsync` is added distinctly; otherwise relax `CalculateAsync` guard)
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollRunStaleGateTests.cs`

**Interfaces:**
- Consumes: `IPayrollRunStalenessEvaluator`.
- Produces: `ValidateAsync`, `SubmitForApprovalAsync`, `ApproveAsync` throw `DomainException` (code `PAYROLL_RUN_STALE`) when stale; `CalculateAsync` allowed from `Draft/Preview/Validated/PendingApproval` and always leaves the run in `Preview`.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact]
public async Task Cannot_approve_a_stale_run()
{
    await using var ctx = TestDb.Create();
    var engine = TestFactory.RunEngine(ctx);
    var (defId, emp, run) = await TestSeed.SubmittedRunWithEmployee(ctx, 2026, 7); // PendingApproval, calc snapshot
    await TestSeed.ApprovedManualDeduction(ctx, emp, 2026, 7, 50m);                // makes it stale
    var act = () => engine.ApproveAsync(run.Id, default);
    (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("stale");
}

[Fact]
public async Task Recalculate_from_validated_resets_to_preview()
{
    await using var ctx = TestDb.Create();
    var engine = TestFactory.RunEngine(ctx);
    var (defId, emp, run) = await TestSeed.ValidatedRunWithEmployee(ctx, 2026, 7);
    var updated = await engine.CalculateAsync(run.Id, default);
    updated.State.Should().Be(PayrollRunState.Preview);
}
```

- [ ] **Step 2: Run tests to verify they fail.** Expected: FAIL (approve succeeds; Calculate rejects Validated).

- [ ] **Step 3: Add a stale-gate helper + relax the Calculate guard.** In `PayrollRunEngine`:
```csharp
private async Task EnsureNotStaleAsync(PayrollRun run, CancellationToken ct)
{
    if (await _staleness.IsStaleAsync(run.Id, ct))
        throw new DomainException("PAYROLL_RUN_STALE: the run is stale — Recalculate to include pending transactions before continuing.");
}
```
Inject `IPayrollRunStalenessEvaluator _staleness`. Call `EnsureNotStaleAsync(run, ct)` at the top of `ValidateAsync`, `SubmitForApprovalAsync`, and `ApproveAsync`. In `CalculateAsync`, change the state guard to allow `Draft, Preview, Validated, PendingApproval`, and after recompute set the state to `Preview` via the existing `ApplyTransition` (which appends a `PayrollRunTransition`); confirm the state machine permits `Validated → Preview` and `PendingApproval → Preview` — if not, add those edges to `PayrollRunStateMachine` (Task 8a below).

- [ ] **Step 3a: If needed, add state-machine edges.** In `PayrollRunStateMachine.cs`, add `Validated → Preview` and `PendingApproval → Preview` to the allowed transitions (recalculation invalidates validation). Add a unit test in `PayrollRunStateMachineTests` asserting these are allowed and that `Approved → Preview` is NOT.

- [ ] **Step 4: Run tests.** Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): stale gate blocks validate/submit/approve; recalculate resets to Preview (bug #4 door 2)"
```

---

## Phase 4 — Transaction provenance (D10)

### Task 9: `PayrollTransaction.Origin` (non-nullable) + `CreatedFromRunId` + args + DTO + migration

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Finance/Entities/PayrollTransaction.cs`
- Modify: `CreatePayrollTransactionArgs` (grep — likely `HR.Application/Engines/Finance/IPayrollTransactionService.cs` or a DTOs file)
- Modify: `PayrollTransactionService.CreateAsync` (stamp `Origin`; default `System`)
- Modify: the transaction EF config (default `Origin`)
- Migration: add columns; backfill `Origin = 0` (System)
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollTransactionOriginTests.cs`

**Interfaces:**
- Produces on `PayrollTransaction`: `PayrollTransactionOrigin Origin` (non-nullable, default `System`), `Guid? CreatedFromRunId`. `CreatePayrollTransactionArgs` gains optional `Origin` (default `System`) and `CreatedFromRunId` (default null) as the **last** positional/optional params so existing callers keep compiling. `PayrollTransactionDto` gains `Origin` and `CreatedFromRunId`.

- [ ] **Step 1: Write the failing test**
```csharp
[Fact]
public async Task Create_defaults_origin_to_system()
{
    await using var ctx = TestDb.Create();
    var svc = TestFactory.TransactionService(ctx);
    var emp = await TestSeed.Employee(ctx);
    var typeId = await TestSeed.DeductionType(ctx, "MANUAL");
    var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
        PayrollTransactionKind.Deduction, emp, typeId, 10m,
        new DateTime(2026,7,15,0,0,0,DateTimeKind.Utc), null, false, null, null, null, SubmitImmediately: false), default);
    var txn = await ctx.PayrollTransactions.FindAsync(id);
    txn!.Origin.Should().Be(PayrollTransactionOrigin.System);
    txn.CreatedFromRunId.Should().BeNull();
}
```

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 3: Add entity properties**
```csharp
public PayrollTransactionOrigin Origin { get; set; } = PayrollTransactionOrigin.System;
public Guid? CreatedFromRunId { get; set; }
```

- [ ] **Step 4: Extend `CreatePayrollTransactionArgs`** — add trailing optional params:
```csharp
// ...existing params...,
PayrollTransactionOrigin Origin = PayrollTransactionOrigin.System,
Guid? CreatedFromRunId = null
```

- [ ] **Step 5: Stamp in `CreateAsync`** — set on the constructed `txn`:
```csharp
Origin = args.Origin,
CreatedFromRunId = args.CreatedFromRunId,
```

- [ ] **Step 6: Add `Origin` + `CreatedFromRunId` to `PayrollTransactionDto`** and to the `Project(...)` mapping in `PayrollTransactionService` (append to the record + constructor call).

- [ ] **Step 7: Configure default + generate migration**
```bash
dotnet ef migrations add PayrollTransactionOriginAndCreatedFromRun \
  --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api
```
Confirm the generated `AddColumn<int>("Origin", ... defaultValue: 0)` (System) so existing rows backfill to `System`; add `AddColumn<Guid?>("CreatedFromRunId", nullable: true)`.

- [ ] **Step 8: Run test + build.** Expected: PASS.

- [ ] **Step 9: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): PayrollTransaction Origin (non-nullable) + CreatedFromRunId provenance"
```

---

## Phase 5 — Validity, validation severity, calculation history (D6, D7, D8)

### Task 10: Structural exclusion reasons computed at Calculate + `PayrollCalculationExclusion`

**Files:**
- Create: `backend/src/HR.Domain/Engines/Finance/Entities/PayrollCalculationExclusion.cs`
- Create: `backend/src/HR.Infrastructure/Engines/Finance/PayrollValidityEvaluator.cs` (pure per-employee validity check)
- Modify: `PayrollRunEngine.CalculateAsync` (evaluate validity for included employees; record exclusions)
- Migration + config
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollValidityTests.cs`

**Interfaces:**
- Produces: `PayrollValidityEvaluator.Evaluate(employeeSnapshot, period) -> PayrollExclusionReasonCode?` (null = valid); `PayrollCalculationExclusion { Guid PayrollRunCalculationId; Guid EmployeeId; PayrollExclusionReasonCode ReasonCode; string? Detail }`.

- [ ] **Step 1: Write the failing test** (unit-test the pure evaluator first — no DB):
```csharp
[Theory]
[InlineData("2026-08-01", null, PayrollExclusionReasonCode.NotEmployedInPeriod)] // hired after period
[InlineData("2020-01-01", "2026-06-30", PayrollExclusionReasonCode.NotEmployedInPeriod)] // left before period
public void Not_employed_in_period_is_excluded(string hire, string? term, PayrollExclusionReasonCode expected)
{
    var v = PayrollValidityEvaluator.Evaluate(
        hireDate: DateTime.Parse(hire), terminationDate: term is null ? null : DateTime.Parse(term),
        basicSalary: 5000m, periodStart: new DateTime(2026,7,1), periodEnd: new DateTime(2026,7,31));
    v.Should().Be(expected);
}

[Fact]
public void No_salary_is_excluded()
    => PayrollValidityEvaluator.Evaluate(new DateTime(2020,1,1), null, 0m,
        new DateTime(2026,7,1), new DateTime(2026,7,31)).Should().Be(PayrollExclusionReasonCode.NoActiveSalary);

[Fact]
public void Employed_with_salary_is_valid()
    => PayrollValidityEvaluator.Evaluate(new DateTime(2020,1,1), null, 5000m,
        new DateTime(2026,7,1), new DateTime(2026,7,31)).Should().BeNull();
```

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 3: Implement the pure evaluator**
```csharp
namespace HR.Infrastructure.Engines.Finance;

public static class PayrollValidityEvaluator
{
    public static PayrollExclusionReasonCode? Evaluate(
        DateTime hireDate, DateTime? terminationDate, decimal basicSalary,
        DateTime periodStart, DateTime periodEnd)
    {
        if (hireDate.Date > periodEnd.Date) return PayrollExclusionReasonCode.NotEmployedInPeriod;
        if (terminationDate is { } t && t.Date < periodStart.Date) return PayrollExclusionReasonCode.NotEmployedInPeriod;
        if (basicSalary <= 0m) return PayrollExclusionReasonCode.NoActiveSalary;
        return null;
    }
}
```
> `AlreadyInActiveRunForPeriod` requires a DB lookup (another active run for the employee in this period); implement that check in `CalculateAsync` where the DbContext is available, not in the pure evaluator.

- [ ] **Step 4: Create `PayrollCalculationExclusion` entity** (fields per Interfaces), add to `ApplicationDbContext`, configure (FK to `PayrollRunCalculation` — created in Task 12; if implementing Task 10 before 12, temporarily FK to run and rewire in Task 12, OR reorder so Task 12 lands first). **Recommended: implement Task 12 before wiring exclusions into the snapshot; keep Task 10 to the pure evaluator + entity, and wire recording in Task 12.**

- [ ] **Step 5: Run tests.** Expected: PASS (evaluator).

- [ ] **Step 6: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): pure payroll validity evaluator + calculation-exclusion entity"
```

---

### Task 11: Validation severity (3 levels; only Error blocks) + rich finding payload + `PayrollCalculationFinding`

**Files:**
- Modify: the validation finding type + `IPayrollValidator` result shape (grep `IPayrollValidator`, `ValidationReport`, `ValidationFinding`)
- Modify: `PayrollValidationEngine` (map to severities; `IsValid` = no `Error`)
- Create: `backend/src/HR.Domain/Engines/Finance/Entities/PayrollCalculationFinding.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollValidationSeverityTests.cs`

**Interfaces:**
- Produces: finding carries `Code, Severity(ValidationSeverity), Message, SuggestedAction, TargetModule, TargetScreen, RelatedEntityType, RelatedEntityId, EmployeeId`. `ValidationReport.IsValid == !findings.Any(f => f.Severity == Error)`. `PayrollCalculationFinding` mirrors these fields + `Guid PayrollRunCalculationId`.

- [ ] **Step 1: Read** the current `ValidationReport`/finding shape and the seven validators in `Validators/`. Note which map to `Error` vs `Warning` (e.g., NegativeSalary=Error, MissingPaymentMethod=Warning, MissingAttendance=Warning, DuplicateEmployee=Error, InvalidGosi=Error, OverlappingPayroll=Error, Currency=Error, RuleConflict=Error).

- [ ] **Step 2: Write the failing test**
```csharp
[Fact]
public void Warnings_do_not_block_validity()
{
    var report = new ValidationReport(new[] {
        new ValidationFinding("MISSING_PAYMENT_METHOD", ValidationSeverity.Warning, "No payment method",
            "Set a payment method", "Employees", "employee-profile", "Employee", Guid.NewGuid(), Guid.NewGuid()) });
    report.IsValid.Should().BeTrue();
}

[Fact]
public void Errors_block_validity()
{
    var report = new ValidationReport(new[] {
        new ValidationFinding("NEGATIVE_NET", ValidationSeverity.Error, "Net < 0",
            "Review deductions", "Payroll", "run", "Employee", Guid.NewGuid(), Guid.NewGuid()) });
    report.IsValid.Should().BeFalse();
}
```

- [ ] **Step 3: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 4: Extend the finding record + `ValidationReport.IsValid`** to the shapes above; update the seven validators to supply `Code/SuggestedAction/TargetModule/TargetScreen/RelatedEntityType/RelatedEntityId` (concrete strings per validator) and the right `Severity`. Create `PayrollCalculationFinding` entity + DbSet + config (FK to `PayrollRunCalculation`).

- [ ] **Step 5: Run all finance tests** to catch validator-signature breakage; fix call sites. Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): 3-severity validation findings (only Error blocks) with deep-link metadata"
```

---

### Task 12: `PayrollRunCalculation` append-only snapshot (versioned, chained, change-summary) written each Calculate

**Files:**
- Create: `backend/src/HR.Domain/Engines/Finance/Entities/PayrollRunCalculation.cs`
- Modify: `ApplicationDbContext` + config (3 tables: calculation, finding, exclusion)
- Modify: `PayrollRunEngine.CalculateAsync` (write snapshot; increment version; chain; change-summary; refresh run pointers; record exclusions from Task 10 + findings from Task 11)
- Migration
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollCalculationSnapshotTests.cs`

**Interfaces:**
- Produces: `PayrollRunCalculation { Guid Id; Guid PayrollRunId; int CalculationVersion; DateTime CalculatedAt; Guid? CalculatedByUserId; string PayrollEngineVersion; Guid PayrollDefinitionVersionId; int EmployeeCount; int IncludedEmployees; int ExcludedEmployees; int TransactionCountConsumed; string ValidationSummary; string FindingSummary; decimal GrossTotal; decimal DeductionTotal; decimal NetTotal; int DurationMs; PayrollCalculationTriggerSource TriggerSource; Guid? PreviousCalculationId; string ChangeSummary; }`.

- [ ] **Step 1: Write the failing test**
```csharp
[Fact]
public async Task Each_calculate_appends_a_monotonic_versioned_snapshot()
{
    await using var ctx = TestDb.Create();
    var engine = TestFactory.RunEngine(ctx);
    var (defId, emp, run) = await TestSeed.DraftRunWithEmployee(ctx, 2026, 7);

    await engine.CalculateAsync(run.Id, default);
    await engine.CalculateAsync(run.Id, default);   // recalc

    var calcs = await ctx.PayrollRunCalculations.Where(c => c.PayrollRunId == run.Id)
        .OrderBy(c => c.CalculationVersion).ToListAsync();
    calcs.Select(c => c.CalculationVersion).Should().Equal(1, 2);
    calcs[1].PreviousCalculationId.Should().Be(calcs[0].Id);
    (await ctx.PayrollRuns.FindAsync(run.Id))!.CurrentCalculationVersion.Should().Be(2);
}
```

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 3: Create the entity + 3 tables** (`PayrollRunCalculation`, and rewire `PayrollCalculationFinding`/`PayrollCalculationExclusion` FKs to `PayrollRunCalculationId`). Add DbSets + configs. Append-only (no update/delete paths).

- [ ] **Step 4: Write the snapshot in `CalculateAsync`.** After the recompute + re-snapshot of payslips, and after computing findings (Task 11) + exclusions (Task 10 + the `AlreadyInActiveRunForPeriod` DB check):
```csharp
var previous = await _db.PayrollRunCalculations
    .Where(c => c.PayrollRunId == run.Id).OrderByDescending(c => c.CalculationVersion).FirstOrDefaultAsync(ct);
var version = (previous?.CalculationVersion ?? 0) + 1;
var calc = new PayrollRunCalculation {
    PayrollRunId = run.Id, CalculationVersion = version,
    CalculatedAt = DateTime.UtcNow, CalculatedByUserId = _user.UserId,
    PayrollEngineVersion = run.CalculationVersion,   // engine/algorithm version string
    PayrollDefinitionVersionId = run.PayrollDefinitionVersionId,
    EmployeeCount = includedCount + excludedCount,
    IncludedEmployees = includedCount, ExcludedEmployees = excludedCount,
    TransactionCountConsumed = consumedCount,
    GrossTotal = run.GrossTotal, DeductionTotal = run.DeductionTotal, NetTotal = run.NetTotal,
    DurationMs = (int)stopwatch.ElapsedMilliseconds,
    TriggerSource = previous is null ? PayrollCalculationTriggerSource.Manual : PayrollCalculationTriggerSource.Recalculate,
    PreviousCalculationId = previous?.Id,
    ValidationSummary = /* e.g. "3 errors, 2 warnings" */,
    FindingSummary = /* top codes */,
    ChangeSummary = BuildChangeSummary(previous, consumedCount, excludedCount /*, finding counts */),
};
_db.PayrollRunCalculations.Add(calc);
// attach findings + exclusions with calc.Id
run.CurrentCalculationVersion = version;
run.LastCalculatedAt = calc.CalculatedAt;
run.LastCalculatedByUserId = calc.CalculatedByUserId;
await _db.SaveChangesAsync(ct);
```
Add a `Stopwatch` around the compute; add a private `BuildChangeSummary(previous, ...)` returning a short human string (e.g. `"+{Δtxn} transactions consumed · {Δexcluded:+#;-#;0} excluded"`). Wrap Calculate in a try/catch to set a "Failed" outcome if needed (surface via the badge — the snapshot is only written on success).

- [ ] **Step 5: Generate migration**
```bash
dotnet ef migrations add PayrollRunCalculationHistory \
  --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api
```

- [ ] **Step 6: Run tests.** Expected: PASS.

- [ ] **Step 7: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): append-only versioned calculation snapshots with findings, exclusions, change-summary"
```

---

## Phase 6 — Read APIs (D9)

### Task 13: Shared paginated query contract

**Files:**
- Create (if absent): `backend/src/HR.Application/Common/Paging/PagedRequest.cs`, `PagedResult.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/PagingContractTests.cs`

**Interfaces:**
- Produces: `record PagedRequest(int Page = 1, int PageSize = 25, string? Sort = null, string? Search = null, string? Filter = null)`; `record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)`; extension `IQueryable<T>.ToPagedResultAsync(PagedRequest, ct)`.

- [ ] **Step 1: Grep** `PagedResult` / `PagedRequest` — if the module already has a paging primitive, **reuse it** and skip creation (record which). Otherwise:

- [ ] **Step 2: Write the failing test**
```csharp
[Fact]
public async Task Paginates_and_reports_total()
{
    var data = Enumerable.Range(1, 60).AsQueryable();
    var page = await data.ToPagedResultAsync(new PagedRequest(Page: 2, PageSize: 25), default);
    page.Total.Should().Be(60);
    page.Items.Should().HaveCount(25);
    page.Items.First().Should().Be(26);
}
```

- [ ] **Step 3–4: Implement** the records + `ToPagedResultAsync` (Skip/Take + `CountAsync`). Run test → PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): shared paged request/result contract for payroll read APIs"
```

---

### Task 14: Summary endpoint `GET runs/{id}` (aggregates + calc metadata + health badge)

**Files:**
- Modify: `backend/src/HR.Modules/Payroll/Controllers/PayrollController.cs` (replace `BuildDetail`'s heavy payload with a summary)
- Modify: `backend/src/HR.Modules/Payroll/DTOs/PayrollDtos.cs` (`PayrollRunSummary`)
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollRunSummaryTests.cs` (service-level) + a controller smoke if a WebApplicationFactory harness exists

**Interfaces:**
- Produces: `record PayrollRunSummary(Guid Id, string RunNumber, DateTime PeriodStart, DateTime PeriodEnd, int TargetPeriodYear, int TargetPeriodMonth, string State, string Currency, RunKpis Kpis, RunCalcMeta Calc, string CalculationStatus, IReadOnlyList<RunTransitionDto> Timeline)`; `record RunKpis(int IncludedEmployees, int ExcludedEmployees, decimal Gross, decimal Deductions, decimal Net, int TransactionsConsumed, int ApprovedNotConsumed)`; `record RunCalcMeta(int Version, DateTime? At, Guid? ByUserId, string? ByUserName)`. `CalculationStatus ∈ {"UpToDate","RecalculationRequired","Failed"}` (server derives UpToDate/RecalculationRequired from staleness; "Calculating" is a client-only transient).

- [ ] **Step 1: Write the failing test** — build a run, calculate, assert summary KPIs come from server aggregates and `CalculationStatus == "UpToDate"`, then add an approved-not-consumed txn and assert `"RecalculationRequired"` and `ApprovedNotConsumed == 1`.

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 3: Implement a `PayrollRunReadService.GetSummaryAsync(runId, ct)`** computing KPIs as SQL aggregates (`CountAsync`, `SumAsync` over payslips/population/transactions) — **no row materialization**. Derive `CalculationStatus` from `IPayrollRunStalenessEvaluator`. Wire `GET runs/{id}` to return it. Move payslip rows OUT of this endpoint (they move to `/employees`, Task 15).

- [ ] **Step 4: Run tests + build.** Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): run summary endpoint with server-side KPI aggregates + calc status"
```

---

### Task 15: Sub-resource endpoints — employees / excluded / validation / transactions / calculations

**Files:**
- Modify: `PayrollController.cs` (+5 GET endpoints, 1 already exists for transactions list — scope it to the run)
- Modify: `PayrollDtos.cs` (row DTOs)
- Test: `backend/tests/HR.Domain.Finance.Tests/PayrollRunSubResourcesTests.cs`

**Interfaces:**
- Produces: `GET /api/payroll/runs/{id}/employees`, `/excluded`, `/validation`, `/transactions`, `/calculations`, `/calculations/{version}` — each `[RequirePermission("Payroll.View")]`, each taking `PagedRequest` and returning `PagedResult<...>`. Transactions rows carry a `Bucket` field `∈ {PendingApproval, ApprovedNotConsumed, Consumed, Posted, Reversed}` computed from status + snapshot membership.

- [ ] **Step 1: Write the failing test** for the transactions bucketing (the highest-risk logic):
```csharp
[Fact]
public async Task Transactions_are_bucketed_by_lifecycle()
{
    await using var ctx = TestDb.Create();
    var read = TestFactory.RunReadService(ctx);
    var (defId, emp, run) = await TestSeed.CalculatedRunWithConsumedTxn(ctx, 2026, 7); // 1 consumed
    await TestSeed.ApprovedManualDeduction(ctx, emp, 2026, 7, 50m);                     // 1 approved-not-consumed
    var page = await read.GetTransactionsAsync(run.Id, new PagedRequest(), default);
    page.Items.Select(t => t.Bucket).Should().Contain(new[] { "Consumed", "ApprovedNotConsumed" });
}
```

- [ ] **Step 2: Run test to verify it fails.** Expected: FAIL.

- [ ] **Step 3: Implement** `PayrollRunReadService` methods:
  - `GetEmployeesAsync` — included population joined to payslips (number/name/dept/gross/deductions/net/item-state), paged.
  - `GetExcludedAsync` — latest calculation's `PayrollCalculationExclusion` rows + scope-excluded population, reason code + label, paged. (**Separate** from validation.)
  - `GetValidationAsync` — latest calculation's `PayrollCalculationFinding` rows (severity, message, suggestedAction, target module/screen, related entity), paged.
  - `GetTransactionsAsync` — resolver-scoped run transactions with `Bucket` (Consumed = id ∈ snapshot TXN set; ApprovedNotConsumed = Approved ∧ in consumable ∧ ∉ snapshot; else by status Posted/Reversed/PendingApproval), paged.
  - `GetCalculationsAsync` / `GetCalculationAsync(version)` — append-only history.
  Wire the six controller endpoints. Reuse the Task 13 paging contract for all.

- [ ] **Step 4: Run tests + build.** Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): paginated run sub-resources (employees/excluded/validation/transactions/calculations)"
```

---

## Phase 7 — Write API + permission (D4, D10)

### Task 16: `Payroll.Transaction.CreateFromRun` permission — seed + grant migration

**Files:**
- Modify: `backend/src/HR.Infrastructure/Persistence/SeedData.cs`
- Migration 1 (seed): insert the permission row (like `20260702003229_AttendancePayrollImpactPermission`)
- Migration 2 (grant): idempotent grant to system roles (like `20260703045706_GrantAttendancePayrollImpactToSystemRoles`)
- Test: `backend/tests/HR.Domain.Finance.Tests/CreateFromRunPermissionSeedTests.cs` (assert `SeedData` includes the key)

**Interfaces:**
- Produces: permission string `Payroll.Transaction.CreateFromRun`.

- [ ] **Step 1: Add to `SeedData.cs`** modules dictionary:
```csharp
["Payroll.Transaction"] = new[] { "CreateFromRun" },
```

- [ ] **Step 2: Generate seed migration**
```bash
dotnet ef migrations add PayrollCreateFromRunPermission \
  --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api
```
Confirm it `InsertData` the new `permissions` row (deterministic Guid = `MD5("Payroll.Transaction.CreateFromRun")`). If EF didn't emit InsertData, add it manually mirroring the 2E permission migration.

- [ ] **Step 3: Hand-author the grant migration** (copy `20260703045706_GrantAttendancePayrollImpactToSystemRoles.cs` structure): scaffold empty then add idempotent SQL granting `Payroll.Transaction / CreateFromRun` to `IsSystemRole` (and a matching `Down`).
```bash
dotnet ef migrations add GrantPayrollCreateFromRunToSystemRoles \
  --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api
```
Fill `Up`/`Down` with the same SQL, changing `Module='Payroll.Transaction' AND Name='CreateFromRun'`.

- [ ] **Step 4: Build + run the seed test.** Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): seed + grant Payroll.Transaction.CreateFromRun permission"
```

---

### Task 17: `POST runs/{id}/transactions` create-from-run (inheritance, EffectiveDate rule, Origin, CreatedFromRunId)

**Files:**
- Modify: `PayrollController.cs` (new POST)
- Create: `backend/src/HR.Infrastructure/Engines/Finance/CreateFromRunService.cs` (+ interface in Application)
- Test: `backend/tests/HR.Domain.Finance.Tests/CreateFromRunTests.cs`

**Interfaces:**
- Produces: `interface ICreateFromRunService { Task<Guid> CreateAsync(Guid runId, CreateFromRunRequest req, CancellationToken ct); }`; `record CreateFromRunRequest(Guid EmployeeId, PayrollTransactionKind Kind, Guid TypeId, decimal Amount, DateTime? EffectiveDate, string? Notes)`. Endpoint `[RequirePermission("Payroll.Transaction.CreateFromRun")]`.

- [ ] **Step 1: Write the failing test**
```csharp
[Fact]
public async Task Create_from_run_inherits_context_and_stamps_provenance()
{
    await using var ctx = TestDb.Create();
    var svc = TestFactory.CreateFromRun(ctx);   // wraps guarded PayrollTransactionService
    var (defId, emp, run) = await TestSeed.DraftRunWithEmployee(ctx, 2026, 7);
    var typeId = await TestSeed.DeductionType(ctx, "MANUAL");

    var id = await svc.CreateAsync(run.Id, new CreateFromRunRequest(
        emp, PayrollTransactionKind.Deduction, typeId, 100m, EffectiveDate: null, Notes: "adj"), default);

    var txn = await ctx.PayrollTransactions.FindAsync(id);
    txn!.Origin.Should().Be(PayrollTransactionOrigin.RunPage);
    txn.CreatedFromRunId.Should().Be(run.Id);
    txn.SourceModule.Should().Be("Manual");
    txn.Status.Should().Be(PayrollTransactionStatus.PendingApproval);
    (txn.TargetPeriodYear, txn.TargetPeriodMonth).Should().Be((2026, 7)); // display columns match run
}

[Fact]
public async Task Supplied_effective_date_outside_run_period_is_rejected()
{
    await using var ctx = TestDb.Create();
    var svc = TestFactory.CreateFromRun(ctx);
    var (defId, emp, run) = await TestSeed.DraftRunWithEmployee(ctx, 2026, 7);
    var typeId = await TestSeed.DeductionType(ctx, "MANUAL");
    var act = () => svc.CreateAsync(run.Id, new CreateFromRunRequest(
        emp, PayrollTransactionKind.Deduction, typeId, 100m,
        EffectiveDate: new DateTime(2026,9,15,0,0,0,DateTimeKind.Utc), Notes: null), default);
    await act.Should().ThrowAsync<DomainException>();
}
```

- [ ] **Step 2: Run tests to verify they fail.** Expected: FAIL.

- [ ] **Step 3: Implement `CreateFromRunService`**
```csharp
public sealed class CreateFromRunService : ICreateFromRunService
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollTransactionService _txns;
    public CreateFromRunService(ApplicationDbContext db, IPayrollTransactionService txns) { _db = db; _txns = txns; }

    public async Task<Guid> CreateAsync(Guid runId, CreateFromRunRequest req, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Run {runId} not found.");
        if (PayrollRunStateMachine.IsImmutable(run.State))
            throw new DomainException("PAYROLL_RUN_IMMUTABLE: cannot add transactions to a closed run.");
        var ver = await _db.PayrollDefinitionVersions.FirstAsync(v => v.Id == run.PayrollDefinitionVersionId, ct);

        // Default EffectiveDate to the run's period end; if supplied, it must resolve to the run period.
        var effective = req.EffectiveDate ?? DateTime.SpecifyKind(run.PeriodEnd, DateTimeKind.Utc);
        var (ry, rm) = PayrollPeriodResolver.Resolve(effective, ver.CutoffDay, ver.CarryToNextPeriod);
        if (ry != run.TargetPeriodYear || rm != run.TargetPeriodMonth)
            throw new DomainException("PAYROLL_EFFECTIVE_DATE_OUT_OF_PERIOD: EffectiveDate must fall in the run's period.");

        return await _txns.CreateAsync(new CreatePayrollTransactionArgs(
            req.Kind, req.EmployeeId, req.TypeId, req.Amount, effective,
            TransactionDate: null, IsRecurring: false, RecurrenceEndDate: null,
            Notes: req.Notes, AttachmentFileId: null, SubmitImmediately: true,
            Origin: PayrollTransactionOrigin.RunPage, CreatedFromRunId: runId), ct);
    }
}
```
> Match `CreatePayrollTransactionArgs` positional order to the record (Task 9). Register in DI. Add the controller POST returning the new id, gated by `Payroll.Transaction.CreateFromRun`.

- [ ] **Step 4: Run tests + build.** Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add backend/src backend/tests
git commit -m "feat(payroll-3): create-from-run endpoint (inherits context, RunPage origin, CreatedFromRunId, PendingApproval)"
```

---

## Phase 8 — Frontend (D5, D9, D10)

> Frontend tasks verify via `npm run build` (from repo root) + manual browser check against the deployed API. RTL + existing shadcn components. Read `AGENTS.md` (Next 16) before editing.

### Task 18: API client — decomposed endpoints + paged contract

**Files:**
- Modify: `src/lib/api/payroll.ts` (+ maybe a new `payroll-run.ts`)
- Modify/confirm types

**Interfaces:**
- Produces: `getRunSummary(id)`, `getRunEmployees(id, paged)`, `getRunExcluded(id, paged)`, `getRunValidation(id, paged)`, `getRunTransactions(id, paged)`, `getRunCalculations(id, paged)`, `createRunTransaction(id, body)`; a shared `Paged<T>` type + `PagedQuery` params matching Task 13.

- [ ] **Step 1:** Add the functions wrapping `apiFetch` against the Task 14/15/17 routes; add TS types mirroring the DTOs (`PayrollRunSummary`, `RunKpis`, `RunCalcMeta`, row types, `TxnBucket` union). Keep money formatting via existing `money()`.
- [ ] **Step 2:** `npm run build` → no type errors.
- [ ] **Step 3: Commit** `git commit -m "feat(payroll-3): frontend API client for decomposed run endpoints"`.

### Task 19: Run page shell — header, calc-status badge, KPI cards, recalc banner

**Files:**
- Modify: `src/app/(dashboard)/payroll/runs/[id]/page.tsx`
- Create: `src/components/payroll/calc-status-badge.tsx`, `src/components/payroll/run-kpi-cards.tsx`

**Interfaces:** consumes `getRunSummary`. Renders header (run number, `StateBadge`, `CalcStatusBadge` for `UpToDate|RecalculationRequired|Calculating|Failed`), 7 KPI cards from `summary.kpis`, and a prominent **"Recalculation Required"** banner when `summary.calculationStatus === "RecalculationRequired"` with **Recalculate** (calls `calculateRun`) and, if `usePermission("Payroll.Approve")`, **Approve & Recalculate**.

- [ ] **Step 1:** Build the two components (real TSX, Tailwind, RTL, lucide icons) + wire into the page; replace the old inline 4-stat block. `Calculating` badge shows while a recalc request is in flight.
- [ ] **Step 2:** `npm run build` → OK; manual: load a run, verify KPIs + badge + banner appear.
- [ ] **Step 3: Commit** `git commit -m "feat(payroll-3): run page header, calc-status badge, KPI cards, recalc banner"`.

### Task 20: Run page panels — Employees / Excluded / Transactions (+ quick-add) / Validation / Timeline

**Files:**
- Modify: `src/app/(dashboard)/payroll/runs/[id]/page.tsx` (tabbed panels)
- Create: `src/components/payroll/run-employees-table.tsx`, `run-excluded-panel.tsx`, `run-transactions-panel.tsx`, `run-validation-panel.tsx`, `run-timeline.tsx`, `quick-add-transaction-dialog.tsx`

**Interfaces:** each panel consumes its paged endpoint (Task 18) with a shared paged table (page/sort/search/filter). Transactions panel groups by `bucket` (PendingApproval / ApprovedNotConsumed / Consumed / Posted / Reversed) and shows **quick-add presets** (Add Deduction / Addition / Attendance Deduction[ABSENCE/LATE/SHORTAGE] / Overtime Addition[OVERTIME]) → `quick-add-transaction-dialog` → `createRunTransaction`; on `422 PAYROLL_PERIOD_CLOSED` show the blocking-run message from the payload. Inline **Approve & Recalculate** on a PendingApproval row when `usePermission("Payroll.Approve")`. **All quick-add controls hidden** when `summary.state` is immutable (`Approved/Executing/Completed/Locked/Archived`) — no disabled buttons. Excluded + Validation rows deep-link to `targetScreen`/related entity. Timeline shows lifecycle transitions + calculation history (from `getRunCalculations`).

- [ ] **Step 1:** Build the panels + dialog (real TSX). Presets pass `kind` + a preset `typeId` (fetch attendance/overtime master-data type ids via existing master-data api; if `OVERTIME` type absent, hide the Overtime preset until 2E seeds it).
- [ ] **Step 2:** Hide quick-add when immutable; wire 422 payload → toast/inline error naming `blockingRunNumber`.
- [ ] **Step 3:** `npm run build` → OK; manual: add a deduction on an open run → appears in ApprovedNotConsumed after approve → "Recalculation Required" → Recalculate → moves to Consumed; verify quick-add hidden on an Approved run.
- [ ] **Step 4: Commit** `git commit -m "feat(payroll-3): run page panels — employees, excluded, transactions+quick-add, validation, timeline"`.

---

## Self-review (author checklist — completed)

- **Spec coverage:** D1 → T2; D2 → T3–T6; D3 → T7–T8; D4 → T17/T19/T20; D5 → T14/T19/T20; D6 → T10/T15; D7 → T11/T15; D8 → T12/T15; D9 → T13–T15; D10 → T9/T16/T17. Permissions (Area 7) → T16; Audit/Origin (Area 8) → T9/T17. All covered.
- **Ordering note:** Task 12 (`PayrollRunCalculation`) is the FK parent for Task 10/11 children — implement 10 (pure evaluator + entity shell) and 11 (finding shape), then land 12 and wire the child FKs there, as flagged in Task 10 Step 4 and Task 12 Step 3.
- **Placeholder scan:** the few `/* ... */` markers (ParseTxnIds format, ValidationSummary/FindingSummary strings, ChangeSummary text) are deliberate "match the existing format found in Step 1" pointers, not skipped logic — each names the exact source to copy. No TBD/TODO logic.
- **Type consistency:** `CreatePayrollTransactionArgs` extended once (T9) with trailing optional `Origin`/`CreatedFromRunId`; all later callers (T5 test, T17) use that shape. `PayrollPeriodResolver.Resolve`, `PayrollRunStateMachine.IsImmutable`, `IPayrollTransactionConsumer.GetConsumableAsync` signatures used verbatim from the grounding read.
- **Deferred (not in any task, by design):** payslips (SP4), exports (SP5), void/amend/reissue (SP6), duplicate detection (SP9).

## Verification before "done"

- `dotnet build backend/src/HR.Api/HR.Api.csproj` — 0 errors.
- `dotnet test backend/tests/HR.Domain.Finance.Tests/HR.Domain.Finance.Tests.csproj` — all green.
- `npm run build` (repo root) — 0 type errors.
- Manual: bug #4 door 1 (approve a txn against an Approved run → 422 `PAYROLL_PERIOD_CLOSED`); door 2 (approve a stale run → 422 `PAYROLL_RUN_STALE`); create-from-run round-trip; excluded/validation/transactions panels paginate.
