using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Modules.Employees.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendanceOvertimeSyncTests
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
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options, new FakeUser());
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static AttendancePayrollSyncService Svc(ApplicationDbContext db)
    {
        var facts = new PayrollFactProvider(db, null!, new AttendanceWageCalculator(db));
        return new AttendancePayrollSyncService(db, facts, new AttendanceWageCalculator(db), new PayrollPeriodGuard(db));
    }

    private static async Task<Guid> SeedAsync(ApplicationDbContext db)
    {
        // Fixed30 basis: dailyWage = 2400/30 = 80, hourlyWage = 10.
        var emp = new Employee { EmployeeNumber = "E1", FirstName = "Ali", LastName = "S", Email = "a@t.local", BasicSalary = 2400m };
        db.Employees.Add(emp);
        foreach (var (obj, code) in new[] {
            (MasterDataObjectType.DeductionType, "ABSENCE"), (MasterDataObjectType.DeductionType, "LATE"),
            (MasterDataObjectType.DeductionType, "SHORTAGE"), (MasterDataObjectType.AdditionType, "OVERTIME") })
            db.MasterDataItems.Add(new MasterDataItem { ObjectType = obj, Code = code, NameAr = code, NameEn = code });
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private static PayrollDefinitionVersion Version(string? calc = null) => new()
    { DayBasis = DayBasis.Fixed30, CutoffDay = 27, CarryToNextPeriod = true, Currency = "SAR", CalcSettingsJson = calc };

    [Fact]
    public async Task Overtime_creates_addition_at_1_5x_when_requested()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,3), Status = AttendanceStatus.Present, OvertimeMinutes = 120 });
        await db.SaveChangesAsync();

        var report = await Svc(db).SyncAsync(Version(), new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31)), new[] { emp }, includeOvertime: true);

        var ot = await db.PayrollTransactions.SingleAsync(t => t.SourceModule == "Attendance" && t.Kind == PayrollTransactionKind.Addition);
        ot.Status.Should().Be(PayrollTransactionStatus.Approved);
        ot.Amount.Should().Be(30m); // 2 hrs * hourlyWage 10 * 1.5
        report.Created.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Overtime_not_created_when_opted_out()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,3), Status = AttendanceStatus.Present, OvertimeMinutes = 120 });
        await db.SaveChangesAsync();

        // includeOvertime null → config default (false)
        await Svc(db).SyncAsync(Version(), new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31)), new[] { emp });

        (await db.PayrollTransactions.AnyAsync(t => t.Kind == PayrollTransactionKind.Addition)).Should().BeFalse();
    }

    [Fact]
    public async Task Overtime_cancels_on_zero()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        var rec = new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,3), Status = AttendanceStatus.Present, OvertimeMinutes = 120 };
        db.AttendanceRecords.Add(rec);
        await db.SaveChangesAsync();
        var period = new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31));
        await Svc(db).SyncAsync(Version(), period, new[] { emp }, includeOvertime: true);

        rec.OvertimeMinutes = 0;
        await db.SaveChangesAsync();
        await Svc(db).SyncAsync(Version(), period, new[] { emp }, includeOvertime: true);

        var additionTxn = await db.PayrollTransactions.SingleAsync(t => t.Kind == PayrollTransactionKind.Addition);
        additionTxn.Status.Should().Be(PayrollTransactionStatus.Cancelled);
    }
}
