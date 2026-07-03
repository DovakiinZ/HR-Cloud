using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
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

public class AttendancePayrollSyncServiceTests
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
        // PayrollFactProvider never dereferences IScopeEngine when an explicit employee population is passed
        // (BuildInputsAsync:41), and the sync service always passes one — so null scope is safe in tests.
        var facts = new PayrollFactProvider(db, null!, new AttendanceWageCalculator(db));
        return new AttendancePayrollSyncService(db, facts, new AttendanceWageCalculator(db), new PayrollPeriodGuard(db));
    }

    // Seeds an employee (30-day-basis daily wage = 3000/30 = 100/day, hourly = 12.5) + the 3 deduction types.
    private static async Task<Guid> SeedAsync(ApplicationDbContext db)
    {
        var emp = new Employee { EmployeeNumber = "E1", FirstName = "Ali", LastName = "S", Email = "a@t.local", BasicSalary = 3000m };
        db.Employees.Add(emp);
        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
            db.MasterDataItems.Add(new MasterDataItem { ObjectType = MasterDataObjectType.DeductionType, Code = code, NameAr = code, NameEn = code });
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private static PayrollDefinitionVersion Version() => new()
    { DayBasis = DayBasis.Fixed30, CutoffDay = 27, CarryToNextPeriod = true, Currency = "SAR" };

    [Fact]
    public async Task Creates_one_approved_record_per_penalty_kind()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent },
            new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,3), Status = AttendanceStatus.Present, LateMinutes = 60 });
        await db.SaveChangesAsync();

        var report = await Svc(db).SyncAsync(Version(), new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31)), new[] { emp }, default);

        report.Created.Should().Be(2); // absence + late (no shortage)
        var txns = await db.PayrollTransactions.Where(t => t.EmployeeId == emp).ToListAsync();
        txns.Should().OnlyContain(t => t.SourceModule == "Attendance" && t.Status == PayrollTransactionStatus.Approved
            && t.Kind == PayrollTransactionKind.Deduction && t.TargetPeriodYear == 2026 && t.TargetPeriodMonth == 7);
        txns.Sum(t => t.Amount).Should().Be(100m + 12.5m); // 1 absent day * 100 + 1 late hr * 12.5
    }

    [Fact]
    public async Task Resync_updates_in_place_and_is_idempotent()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent });
        await db.SaveChangesAsync();
        var period = new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31));

        var first = await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);
        var second = await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        first.Created.Should().Be(1);
        second.Created.Should().Be(0);
        second.Updated.Should().Be(1);
        (await db.PayrollTransactions.CountAsync(t => t.EmployeeId == emp && t.Status == PayrollTransactionStatus.Approved))
            .Should().Be(1); // no duplicate
    }

    [Fact]
    public async Task Cleared_penalty_cancels_the_stale_record()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        var rec = new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent };
        db.AttendanceRecords.Add(rec);
        await db.SaveChangesAsync();
        var period = new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31));
        await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        rec.Status = AttendanceStatus.Present; // correction cleared the absence
        await db.SaveChangesAsync();
        var report = await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        report.Removed.Should().Be(1);
        (await db.PayrollTransactions.SingleAsync(t => t.EmployeeId == emp)).Status
            .Should().Be(PayrollTransactionStatus.Cancelled);
    }

    [Fact]
    public async Task Posted_record_is_skipped_not_touched()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent });
        await db.SaveChangesAsync();
        var period = new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31));
        await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        var txn = await db.PayrollTransactions.SingleAsync(t => t.EmployeeId == emp);
        txn.Status = PayrollTransactionStatus.Posted; // simulate a completed run
        await db.SaveChangesAsync();
        var report = await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        report.SkippedPosted.Should().Be(1);
        report.Updated.Should().Be(0);
    }

    [Fact]
    public async Task Reversed_record_is_skipped_not_mutated()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent });
        await db.SaveChangesAsync();
        var period = new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31));
        await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        var txn = await db.PayrollTransactions.SingleAsync(t => t.EmployeeId == emp);
        txn.Status = PayrollTransactionStatus.Reversed;
        txn.Amount = 999m; // sentinel — must not be overwritten by re-sync
        await db.SaveChangesAsync();
        var report = await Svc(db).SyncAsync(Version(), period, new[] { emp }, default);

        report.SkippedPosted.Should().Be(1);
        report.Updated.Should().Be(0);
        var after = await db.PayrollTransactions.SingleAsync(t => t.EmployeeId == emp);
        after.Amount.Should().Be(999m);
        after.Status.Should().Be(PayrollTransactionStatus.Reversed);
    }

    [Fact]
    public async Task Toggle_off_is_a_noop()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedAsync(db);
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent });
        await db.SaveChangesAsync();
        var v = Version();
        v.CalcSettingsJson = "{\"includeAttendanceDeductions\":false}";

        var report = await Svc(db).SyncAsync(v, new PayrollPeriod(Utc(2026,7,1), Utc(2026,7,31)), new[] { emp }, default);

        report.TotalProcessed.Should().Be(0);
        (await db.PayrollTransactions.AnyAsync(t => t.EmployeeId == emp)).Should().BeFalse();
    }
}
