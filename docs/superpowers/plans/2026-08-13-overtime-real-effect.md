# Overtime Request → Real Effect (Increment 1: Pay Branch) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Approving an `OVERTIME_REQUEST` creates a born-Approved `OVERTIME` payroll Addition for the approved overtime hours, at the KSA 1.5× rate, posted to the overtime date's payroll month, paid exactly once.

**Architecture:** Mirror the existing `AttendancePermissionCreateExecutor` slice. A new `IOvertimeWageResolver` computes the hourly wage + overtime multiplier; a new `OvertimeAdditionExecutor` (auto-registered via DI assembly scanning) writes the `PayrollTransaction`. Re-wire `OVERTIME_REQUEST` from the mis-wired `Attendance.Correct` to the new `Overtime.CreateAddition` effect and bump the provisioning seed version. No schema migration (config rows + reused `PayrollTransaction` table).

**Tech Stack:** .NET 8, EF Core, xUnit, EF InMemory provider (tests).

## Global Constraints

- No new EF migration — reuse `PayrollTransaction`; `OVERTIME` AdditionType is already seeded in `MasterDataDefaults`.
- Executor MUST NOT call `SaveChanges` — the completion engine commits (per `IEffectExecutor` contract).
- Rate: `amount = round(hours × hourlyWage × overtimeMultiplier, 2)`; `hourlyWage = BasicSalary / 30 / 8`; `overtimeMultiplier` default **1.5** (KSA Labor Law Art. 107), overridable via `CalcSettingsJson.attendanceRates.overtimeMultiplier`.
- Pay exactly once: idempotent per request instance + a guard against the engine `includeOvertime` sync.
- Full backend suite must stay green (baseline 744 pass / 62 skip).
- Commit after every task. Branch: `feat/overtime-real-effect`.

---

### Task 1: `IOvertimeWageResolver` + `OvertimeWageResolver`

Computes the per-employee hourly wage and the tenant's overtime multiplier, so the executor stays free of wage math. Mirrors `IUnpaidPermissionWageResolver` (same folder, same DI spot).

**Files:**
- Create: `backend/src/HR.Modules/Attendance/Services/OvertimeWageResolver.cs`
- Modify: `backend/src/HR.Modules/Attendance/DependencyInjection/DependencyInjection.cs` (register next to `IUnpaidPermissionWageResolver`, ~line 16)
- Test: `backend/tests/HR.Domain.Finance.Tests/OvertimeWageResolverTests.cs`

**Interfaces:**
- Produces: `IOvertimeWageResolver.ResolveAsync(Guid employeeId, CancellationToken ct) → Task<(decimal HourlyWage, decimal OvertimeMultiplier)>`.
  - `HourlyWage = BasicSalary / 30m / 8m`.
  - `OvertimeMultiplier = PayrollCalcSettings.Rates(<latest published version CalcSettingsJson>).Overtime` (default `1.5m` when no published version / absent key).

- [ ] **Step 1: Write the failing test**

Create `backend/tests/HR.Domain.Finance.Tests/OvertimeWageResolverTests.cs`:

```csharp
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Finance.Entities;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class OvertimeWageResolverTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    private static async Task<Guid> SeedEmployeeAsync(ApplicationDbContext db, decimal basic)
    {
        var emp = new Employee
        {
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName = "Ali", LastName = "Test",
            Email = $"{Guid.NewGuid():N}@t.local",
            BasicSalary = basic,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    [Fact]
    public async Task HourlyWage_is_basic_over_30_over_8_and_default_multiplier_is_1_5()
    {
        await using var db = Ctx($"otw-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db, 7200m); // 7200/30/8 = 30/hr

        var (hourly, mult) = await new OvertimeWageResolver(db).ResolveAsync(emp, default);

        Assert.Equal(30m, hourly);
        Assert.Equal(1.5m, mult);
    }

    [Fact]
    public async Task Multiplier_reads_overtimeMultiplier_from_latest_published_version()
    {
        await using var db = Ctx($"otw-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db, 7200m);
        db.PayrollDefinitionVersions.Add(new PayrollDefinitionVersion
        {
            PayrollDefinitionId = Guid.NewGuid(),
            VersionNumber = 1,
            PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CalcSettingsJson = "{\"attendanceRates\":{\"overtimeMultiplier\":2.0}}",
        });
        await db.SaveChangesAsync();

        var (_, mult) = await new OvertimeWageResolver(db).ResolveAsync(emp, default);

        Assert.Equal(2.0m, mult);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\tests\HR.Domain.Finance.Tests\HR.Domain.Finance.Tests.csproj" --filter "FullyQualifiedName~OvertimeWageResolverTests"`
Expected: FAIL — `OvertimeWageResolver` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/HR.Modules/Attendance/Services/OvertimeWageResolver.cs`:

```csharp
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Services;

/// <summary>Resolves the wage basis for overtime pay: hourly wage (BasicSalary / 30 / 8) and the
/// tenant overtime multiplier (CalcSettingsJson.attendanceRates.overtimeMultiplier, default 1.5 =
/// KSA Labor Law Art. 107) read from the latest published payroll definition version.</summary>
public interface IOvertimeWageResolver
{
    Task<(decimal HourlyWage, decimal OvertimeMultiplier)> ResolveAsync(
        Guid employeeId, CancellationToken ct = default);
}

/// <summary>EF Core / DB-backed implementation of <see cref="IOvertimeWageResolver"/>.</summary>
public sealed class OvertimeWageResolver : IOvertimeWageResolver
{
    private readonly ApplicationDbContext _db;

    public OvertimeWageResolver(ApplicationDbContext db) => _db = db;

    public async Task<(decimal HourlyWage, decimal OvertimeMultiplier)> ResolveAsync(
        Guid employeeId, CancellationToken ct = default)
    {
        var basic = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.BasicSalary)
            .FirstOrDefaultAsync(ct);

        var hourly = basic / 30m / 8m;

        var calcJson = await _db.PayrollDefinitionVersions.AsNoTracking()
            .Where(v => v.PublishedAt != null)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => v.CalcSettingsJson)
            .FirstOrDefaultAsync(ct);

        var multiplier = PayrollCalcSettings.Rates(calcJson).Overtime;

        return (hourly, multiplier);
    }
}
```

- [ ] **Step 4: Register in DI**

In `backend/src/HR.Modules/Attendance/DependencyInjection/DependencyInjection.cs`, directly below the `IUnpaidPermissionWageResolver` registration (~line 16), add:

```csharp
        services.AddScoped<IOvertimeWageResolver, OvertimeWageResolver>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\tests\HR.Domain.Finance.Tests\HR.Domain.Finance.Tests.csproj" --filter "FullyQualifiedName~OvertimeWageResolverTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Modules/Attendance/Services/OvertimeWageResolver.cs backend/src/HR.Modules/Attendance/DependencyInjection/DependencyInjection.cs backend/tests/HR.Domain.Finance.Tests/OvertimeWageResolverTests.cs
git commit -m "feat(overtime): IOvertimeWageResolver — hourly wage + KSA overtime multiplier"
```

---

### Task 2: `Overtime.CreateAddition` effect type + `OvertimeAdditionExecutor`

The core: on final approval, create a born-Approved `OVERTIME` Addition. Auto-registered by assembly scanning (no DI edit — it lives in the already-scanned `HR.Modules.Attendance.Completion` assembly, like `AttendancePermissionCreateExecutor`).

**Files:**
- Modify: `backend/src/HR.Application/Engines/Completion/EffectTypes.cs` (add the constant)
- Create: `backend/src/HR.Modules/Attendance/Completion/OvertimeAdditionExecutor.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/OvertimeAdditionExecutorTests.cs`

**Interfaces:**
- Consumes: `IOvertimeWageResolver.ResolveAsync` (Task 1); `IPayrollPeriodGuard.EnsurePeriodOpenForAsync(Guid, DateTime, CancellationToken)` (throws `PayrollPeriodClosedException` when closed); `EffectContext` readers `Date("date")`, `Dec("hours")`, `Str("reason")`, `EmployeeId`, `ActorUserId`, `RequestInstanceId`.
- Produces: `OvertimeAdditionExecutor.EffectType == "Overtime.CreateAddition"`; writes a `PayrollTransaction { Kind = Addition, ReferenceType = "OvertimeRequest", ReferenceId = RequestInstanceId, SourceModule = "Overtime" }`.

- [ ] **Step 1: Add the effect-type constant**

In `backend/src/HR.Application/Engines/Completion/EffectTypes.cs`, under the `// Payroll-adjacent` group (after `LoanCreate`), add:

```csharp
    // Overtime → payroll Addition (KSA 1.5× on the approved overtime hours)
    public const string OvertimeCreateAddition = "Overtime.CreateAddition";
```

- [ ] **Step 2: Write the failing tests**

Create `backend/tests/HR.Domain.Finance.Tests/OvertimeAdditionExecutorTests.cs`:

```csharp
using System.Text.Json;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Completion;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class OvertimeAdditionExecutorTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private sealed class OpenPeriodGuard : IPayrollPeriodGuard
    {
        public Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ClosedPeriodGuard : IPayrollPeriodGuard
    {
        public Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct = default)
            => throw new PayrollPeriodClosedException("closed");
    }

    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    private static OvertimeAdditionExecutor Executor(ApplicationDbContext db, IPayrollPeriodGuard? guard = null)
        => new(db, new OvertimeWageResolver(db), guard ?? new OpenPeriodGuard());

    private static async Task<Guid> SeedEmployeeAsync(ApplicationDbContext db, decimal basic = 7200m)
    {
        var emp = new Employee
        {
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName = "Ali", LastName = "Test",
            Email = $"{Guid.NewGuid():N}@t.local",
            BasicSalary = basic,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private static async Task<Guid> SeedOvertimeTypeAsync(ApplicationDbContext db)
    {
        var item = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.AdditionType,
            Code = "OVERTIME",
            NameAr = "عمل إضافي", NameEn = "Overtime",
            IsActive = true,
        };
        db.MasterDataItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static EffectContext Context(Guid employeeId, Guid requestId, object payload) => new()
    {
        RequestInstanceId = requestId,
        RequestNumber = "REQ-1",
        RequestTypeCode = "OVERTIME_REQUEST",
        EmployeeId = employeeId,
        ActorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    [Fact] // 5h × (7200/30/8 = 30) × 1.5 = 225
    public async Task Creates_approved_overtime_addition_at_ksa_rate()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        await SeedOvertimeTypeAsync(db);

        var result = await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", hours = "5", reason = "peak" }), default);
        await db.SaveChangesAsync();

        Assert.False(result.IsSkipped);
        var txn = await db.PayrollTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(PayrollTransactionKind.Addition, txn.Kind);
        Assert.Equal(225m, txn.Amount);
        Assert.Equal(PayrollTransactionStatus.Approved, txn.Status);
        Assert.Equal(2026, txn.TargetPeriodYear);
        Assert.Equal(8, txn.TargetPeriodMonth);
        Assert.Equal("OvertimeRequest", txn.ReferenceType);
    }

    [Fact]
    public async Task Uses_configured_multiplier()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        await SeedOvertimeTypeAsync(db);
        db.PayrollDefinitionVersions.Add(new PayrollDefinitionVersion
        {
            PayrollDefinitionId = Guid.NewGuid(), VersionNumber = 1,
            PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CalcSettingsJson = "{\"attendanceRates\":{\"overtimeMultiplier\":2.0}}",
        });
        await db.SaveChangesAsync();

        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", hours = "5", reason = "x" }), default);
        await db.SaveChangesAsync();

        var txn = await db.PayrollTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(300m, txn.Amount); // 5 × 30 × 2.0
    }

    [Fact]
    public async Task Is_idempotent_per_request_instance()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        await SeedOvertimeTypeAsync(db);
        var reqId = Guid.NewGuid();
        var payload = new { date = "2026-08-03", hours = "5", reason = "x" };

        await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync();
        var second = await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync();

        Assert.True(second.IsSkipped);
        Assert.Equal(1, await db.PayrollTransactions.CountAsync());
    }

    [Fact]
    public async Task Skips_when_engine_sync_already_paid_the_period()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        var typeId = await SeedOvertimeTypeAsync(db);
        db.PayrollTransactions.Add(new PayrollTransaction
        {
            Kind = PayrollTransactionKind.Addition, EmployeeId = emp, TypeId = typeId, Amount = 100m,
            EffectiveDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            TargetPeriodYear = 2026, TargetPeriodMonth = 8,
            SourceModule = "Attendance", ReferenceType = "AttendancePeriodPenalty",
            Status = PayrollTransactionStatus.Approved,
        });
        await db.SaveChangesAsync();

        var result = await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", hours = "5", reason = "x" }), default);
        await db.SaveChangesAsync();

        Assert.True(result.IsSkipped);
        Assert.Equal(1, await db.PayrollTransactions.CountAsync()); // no second txn
    }

    [Fact]
    public async Task Finalized_period_emits_notification_and_creates_no_addition()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        await SeedOvertimeTypeAsync(db);

        var result = await Executor(db, new ClosedPeriodGuard()).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", hours = "5", reason = "x" }), default);
        await db.SaveChangesAsync();

        Assert.True(result.IsSkipped);
        Assert.Equal(0, await db.PayrollTransactions.CountAsync());
        var note = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal("PayrollAdjustmentNeeded", note.Category);
    }

    [Fact]
    public async Task Rejects_non_positive_hours()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        await SeedOvertimeTypeAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", hours = "0", reason = "x" }), default));
    }

    [Fact]
    public async Task Throws_when_overtime_addition_type_unseeded()
    {
        await using var db = Ctx($"ot-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);
        // no OVERTIME AdditionType seeded

        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", hours = "5", reason = "x" }), default));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\tests\HR.Domain.Finance.Tests\HR.Domain.Finance.Tests.csproj" --filter "FullyQualifiedName~OvertimeAdditionExecutorTests"`
Expected: FAIL — `OvertimeAdditionExecutor` does not exist (compile error).

- [ ] **Step 4: Write the executor**

Create `backend/src/HR.Modules/Attendance/Completion/OvertimeAdditionExecutor.cs`:

```csharp
using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: on final approval of an overtime request, create a born-Approved OVERTIME
/// payroll Addition for the approved hours, at the KSA overtime rate. Paid exactly once: idempotent
/// per request instance, and guarded against the engine includeOvertime sync (which writes
/// SourceModule="Attendance"). Closed target period → PayrollAdjustmentNeeded notification, no
/// mutation.</summary>
public sealed class OvertimeAdditionExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IOvertimeWageResolver _wage;
    private readonly IPayrollPeriodGuard _guard;

    public OvertimeAdditionExecutor(ApplicationDbContext db, IOvertimeWageResolver wage, IPayrollPeriodGuard guard)
    {
        _db = db;
        _wage = wage;
        _guard = guard;
    }

    public string EffectType => EffectTypes.OvertimeCreateAddition;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind(
            (ctx.Date("date") ?? throw Validation("date", "تاريخ العمل الإضافي مطلوب / Overtime date is required.")).Date,
            DateTimeKind.Utc);

        var hours = ctx.Dec("hours");
        if (hours <= 0m)
            throw Validation("hours", "عدد ساعات العمل الإضافي يجب أن يكون أكبر من صفر / Overtime hours must be greater than zero.");

        // ── Idempotency: one Addition per approved request instance ──────────────────────────────────
        var already = await _db.PayrollTransactions.AnyAsync(
            t => t.ReferenceType == "OvertimeRequest" && t.ReferenceId == ctx.RequestInstanceId, ct);
        if (already)
            return EffectExecutionResult.Skip("AlreadyApplied",
                targetEntityType: nameof(PayrollTransaction),
                summary: $"Overtime addition for request {ctx.RequestNumber} already created.");

        // ── Resolve the OVERTIME AdditionType (must be seeded) ───────────────────────────────────────
        var typeId = await _db.MasterDataItems
            .Where(m => m.ObjectType == MasterDataObjectType.AdditionType && m.Code == "OVERTIME")
            .Select(m => m.Id)
            .FirstOrDefaultAsync(ct);
        if (typeId == Guid.Empty)
            throw new NonRetryableEffectException(
                "OVERTIME addition type is not seeded; run master-data seed-defaults. " +
                "/ نوع الإضافة OVERTIME غير مُهيأ؛ شغّل تهيئة البيانات الأساسية.");

        // ── Double-pay guard: skip if the engine sync already paid overtime for this period ──────────
        var engineAlreadyPaid = await _db.PayrollTransactions.AnyAsync(
            t => t.EmployeeId == ctx.EmployeeId
              && t.TypeId == typeId
              && t.TargetPeriodYear == date.Year
              && t.TargetPeriodMonth == date.Month
              && t.SourceModule == "Attendance"
              && t.Status != PayrollTransactionStatus.Cancelled, ct);
        if (engineAlreadyPaid)
            return EffectExecutionResult.Skip("EngineOvertimeAlreadyPaid",
                targetEntityType: nameof(PayrollTransaction),
                summary: $"Engine overtime sync already paid {date:yyyy-MM} for this employee; request skipped to avoid double pay.");

        // ── Period guard: no born-Approved transaction into a frozen period ──────────────────────────
        bool periodClosed = false;
        try { await _guard.EnsurePeriodOpenForAsync(ctx.EmployeeId, date, ct); }
        catch (PayrollPeriodClosedException) { periodClosed = true; }

        var (hourlyWage, multiplier) = await _wage.ResolveAsync(ctx.EmployeeId, ct);
        var amount = Math.Round(hours * hourlyWage * multiplier, 2);

        if (periodClosed)
        {
            if (ctx.ActorUserId is { } signalUser)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = signalUser,
                    TitleAr = "عمل إضافي — فترة رواتب مقفلة",
                    TitleEn = "Overtime — Payroll Period Finalized",
                    BodyAr = $"تمت الموافقة على عمل إضافي ({hours:0.##} ساعة، {amount:0.##} ريال) بتاريخ {date:yyyy-MM-dd} لكن فترة الرواتب مقفلة. يلزم تسوية يدوية.",
                    BodyEn = $"Overtime ({hours:0.##}h, {amount:0.##} SAR) approved for {date:yyyy-MM-dd} but the payroll period is finalized. A manual payroll adjustment is required.",
                    Category = "PayrollAdjustmentNeeded",
                    Link = "/payroll",
                    IsRead = false,
                });
            }
            return EffectExecutionResult.Skip("PayrollPeriodFinalized",
                targetEntityType: nameof(PayrollTransaction),
                summary: $"Overtime for {date:yyyy-MM-dd} not posted — period finalized; adjustment notification emitted.");
        }

        _db.PayrollTransactions.Add(new PayrollTransaction
        {
            Kind = PayrollTransactionKind.Addition,
            EmployeeId = ctx.EmployeeId,
            TypeId = typeId,
            Amount = amount,
            EffectiveDate = date,
            TransactionDate = date,
            TargetPeriodYear = date.Year,
            TargetPeriodMonth = date.Month,
            SourceModule = "Overtime",
            ReferenceType = "OvertimeRequest",
            ReferenceId = ctx.RequestInstanceId,
            Status = PayrollTransactionStatus.Approved,
            Origin = PayrollTransactionOrigin.System,
            Notes = ctx.Str("reason"),
        });

        return EffectExecutionResult.Ok(
            targetEntityType: nameof(PayrollTransaction),
            after: new { EmployeeId = ctx.EmployeeId, Amount = amount, date.Year, date.Month, Hours = hours },
            summary: $"Overtime addition {amount:0.##} SAR ({hours:0.##}h) recorded for {date:yyyy-MM}.");
    }

    private static ValidationException Validation(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\tests\HR.Domain.Finance.Tests\HR.Domain.Finance.Tests.csproj" --filter "FullyQualifiedName~OvertimeAdditionExecutorTests"`
Expected: PASS (7 tests).

> Confirmed signatures: `EffectExecutionResult.Ok(string? targetEntityType = null, Guid? targetRecordId = null, object? after = null, string? summary = null)` and `Skip(string reason, string? targetEntityType = null, string? summary = null)` — the calls above omit `targetRecordId` (unknown pre-save). `PayrollTransaction.Notes`, `TargetPeriodYear/Month` (`int?`), `ReferenceType/Id`, `SourceModule`, `Origin`, `Status` all exist on the entity.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Application/Engines/Completion/EffectTypes.cs backend/src/HR.Modules/Attendance/Completion/OvertimeAdditionExecutor.cs backend/tests/HR.Domain.Finance.Tests/OvertimeAdditionExecutorTests.cs
git commit -m "feat(overtime): OvertimeAdditionExecutor — approved hours -> born-Approved OVERTIME Addition, paid once"
```

---

### Task 3: Re-wire `OVERTIME_REQUEST`, add catalog descriptor, bump seed version

Swap the request's required effect from the mis-wired `Attendance.Correct` (drops `hours`) to `Overtime.CreateAddition`, expose it in the effect-builder catalog, and bump the provisioning seed version so re-provision reconciles it.

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs:93-98`
- Modify: `backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs` (add a descriptor to the `Descriptors[]` array)
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs:53` (`CurrentSeedVersion` 6 → 7)
- Test: `backend/tests/HR.Modules.Platform.Tests/Requests/OvertimeEffectWiringTests.cs`

**Interfaces:**
- Consumes: `EffectTypes.OvertimeCreateAddition` (Task 2).
- Produces: `SystemRequestEffects.Required["OVERTIME_REQUEST"]` maps to `Overtime.CreateAddition` with inputs `date`←`startDate`, `hours`←`hours`, `reason`←`reason`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/HR.Modules.Platform.Tests/Requests/OvertimeEffectWiringTests.cs`:

```csharp
using HR.Application.Engines.Completion;
using HR.Modules.Platform.Services.Requests;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

public class OvertimeEffectWiringTests
{
    [Fact]
    public void Overtime_request_is_wired_to_the_overtime_addition_effect()
    {
        var specs = SystemRequestEffects.Required["OVERTIME_REQUEST"];
        var overtime = Assert.Single(specs);

        Assert.Equal(EffectTypes.OvertimeCreateAddition, overtime.EffectType);
        Assert.Equal("startDate", overtime.Inputs["date"].Key);
        Assert.Equal("hours", overtime.Inputs["hours"].Key);
        Assert.Equal("reason", overtime.Inputs["reason"].Key);
    }
}
```

> Confirmed shapes: `RequiredEffectSpec(string EffectType, EffectTrigger Trigger, EffectExecutionMode Mode, Dictionary<string, EffectValueMapping> Inputs)` and `EffectValueMapping { EffectValueSource Source; string Key; }` — so `spec.EffectType` and `spec.Inputs["date"].Key` are correct.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\tests\HR.Modules.Platform.Tests\HR.Modules.Platform.Tests.csproj" --filter "FullyQualifiedName~OvertimeEffectWiringTests"`
Expected: FAIL — currently wired to `EffectTypes.AttendanceCorrect`, and there is no `hours` input.

- [ ] **Step 3: Re-wire the request**

In `backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs`, replace the `OVERTIME_REQUEST` block (lines 93-98):

```csharp
            ["OVERTIME_REQUEST"] = new[]
            {
                Transactional(EffectTypes.OvertimeCreateAddition, Map(
                    ("date", Field("startDate")),
                    ("hours", Field("hours")),
                    ("reason", Field("reason")))),
            },
```

- [ ] **Step 4: Add the catalog descriptor**

In `backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs`, add a new element to the `Descriptors[]` array (after the `AttendanceCreatePermission` descriptor, ~line 125):

```csharp
        new()
        {
            EffectType = EffectTypes.OvertimeCreateAddition,
            LabelAr = "احتساب عمل إضافي", LabelEn = "Record overtime pay",
            DescriptionAr = "ينشئ إضافة راتب معتمدة للعمل الإضافي بمعدل 1.5 على الساعة، تُرحّل لشهر الراتب.",
            DescriptionEn = "Creates an approved overtime payroll addition (1.5× hourly) posted to the overtime month.",
            Module = "Payroll",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("date", "التاريخ", "Date", true, FieldOrContext),
                In("hours", "عدد الساعات", "Hours", true, FieldOrContext),
                In("reason", "السبب", "Reason", false, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Payroll.Create" },
        },
```

- [ ] **Step 5: Bump the seed version**

In `backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs:53`, change:

```csharp
    public const int CurrentSeedVersion = 7;
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\tests\HR.Modules.Platform.Tests\HR.Modules.Platform.Tests.csproj" --filter "FullyQualifiedName~OvertimeEffectWiringTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs backend/tests/HR.Modules.Platform.Tests/Requests/OvertimeEffectWiringTests.cs
git commit -m "feat(overtime): re-wire OVERTIME_REQUEST to Overtime.CreateAddition + catalog descriptor + seed v7"
```

---

### Task 4: Full-suite regression + push

**Files:** none (verification only)

- [ ] **Step 1: Run the whole backend suite**

Run: `dotnet test "D:\HR-Cloud-main\HR-Cloud-main\backend\HR.sln"`
Expected: all green — baseline 744 + 11 new (2 resolver + 7 executor + 1 wiring, and any Ok-signature adjustment). 0 failed.

- [ ] **Step 2: Push the branch to both remotes**

```bash
git push -u origin feat/overtime-real-effect
git push -u sanad feat/overtime-real-effect
```

- [ ] **Step 3: Stop for review** — request a whole-branch code review before opening a PR / deploying.

---

## Deploy (user-gated, after merge)

No schema migration. API zip-redeploy to `hrcloud-api-v4xd`; re-provision the tenant so SeedVersion 6→7
re-wires the overtime effect; behavioral verify: submit an overtime request → approve → confirm an
approved `OVERTIME` Addition on the target month's payroll.

## Deferred — Increment 2 (separate spec/plan)

Compensatory-leave branch: a `compensationType` form field (Pay | CompLeave), a comp-leave `LeaveType`
seed, an hours→days rule, and a leave-credit executor writing `LeaveBalanceTransaction { Type = Accrual }`.
Not built here — no leave-credit path exists yet.
