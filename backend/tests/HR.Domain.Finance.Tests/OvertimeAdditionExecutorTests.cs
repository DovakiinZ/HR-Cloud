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
            => throw new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
                "PAYROLL_PERIOD_CLOSED", Guid.NewGuid(), "PR-1", Guid.NewGuid(), effectiveDate.Year, effectiveDate.Month, "Locked"));
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
