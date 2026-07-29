using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Modules.Attendance.DTOs;
using HR.Modules.Attendance.Services;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionServiceTests
{
    // ── Fakes (mirror AttendanceCorrectionExecutorTests) ─────────────────────

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

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Seed an Active employee with a fixed day shift start→end, required minutes;
    /// returns the employee Guid. Mirrors the real-service test in AttendanceCorrectionExecutorTests.</summary>
    private static async Task<Guid> SeedEmployeeWithShiftAsync(
        ApplicationDbContext db,
        TimeOnly start, TimeOnly end, int required,
        DateTime effectiveFrom)
    {
        var emp = new Employee
        {
            EmployeeNumber = $"E-PERM-{Guid.NewGuid():N}",
            FirstName = "Ali", LastName = "Test",
            Email = $"{Guid.NewGuid():N}@t.local",
            BasicSalary = 5000m,
            // Status defaults to EmployeeStatus.Active
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var shift = new Shift
        {
            NameAr = "دوام صباحي", NameEn = "Day Shift",
            StartTime = start,
            EndTime = end,
            RequiredMinutes = required,
            BreakMinutes = 0,
            GraceAfterStartMinutes = 0,
            IsFlexible = false,
            IsActive = true,
            WeekendDays = "5,6", // Fri+Sat; 2026-08-03 is Monday → working day
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        db.ShiftAssignments.Add(new ShiftAssignment
        {
            ShiftId = shift.Id,
            EmployeeId = emp.Id,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            Priority = 10,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        return emp.Id;
    }

    private static AttendanceService Service(ApplicationDbContext db) =>
        new(db, new FakeUser(), new AttendanceCalculationService(), new ShiftResolver());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recalc_excuses_shortage_covered_by_an_approved_permission()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");

        var emp = await SeedEmployeeWithShiftAsync(db,
            start: new TimeOnly(8, 0), end: new TimeOnly(16, 0), required: 480,
            effectiveFrom: Utc(2026, 1, 1));

        // Day worked 08:00–14:00 → 120 shortage, no permission yet.
        var recId = await Service(db).AddManualPunchAsync(
            new ManualPunchRequest
            {
                EmployeeId = emp,
                Date = new DateTime(2026, 8, 3),
                CheckIn = "08:00",
                CheckOut = "14:00"
            }, default);

        var before = await db.AttendanceRecords.AsNoTracking().FirstAsync(r => r.Id == recId);
        Assert.Equal(120, before.ShortageMinutes);

        // Approve a permission 14:00–16:00 (840→960 minutes-from-midnight), then force a recalc
        // via CorrectAsync with the same punch times (idempotent times, triggers RecalcAsync).
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp,
            Date = new DateTime(2026, 8, 3),
            FromMinutes = 840,   // 14:00
            ToMinutes = 960,     // 16:00
            ExcusedMinutes = 120,
            Source = AttendanceSources.AttendancePermission,
            RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        // Correct with the same times → triggers RecalcAsync which should now read the permission.
        await Service(db).CorrectAsync(recId,
            new CorrectAttendanceRequest { CheckIn = "08:00", CheckOut = "14:00", Reason = "recalc" },
            default);

        var after = await db.AttendanceRecords.AsNoTracking().FirstAsync(r => r.Id == recId);
        Assert.Equal(0, after.ShortageMinutes);        // durable across recalc
        Assert.Equal(120, after.ExcusedMinutes);
        Assert.Equal(AttendanceStatus.Present, after.Status);
    }

    [Fact]
    public async Task GetRangeRows_display_path_also_honors_permission()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");

        var emp = await SeedEmployeeWithShiftAsync(db,
            start: new TimeOnly(8, 0), end: new TimeOnly(16, 0), required: 480,
            effectiveFrom: Utc(2026, 1, 1));

        // Persist a record: 08:00–14:00 → 120 shortage (stored).
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = emp,
            Date = Utc(2026, 8, 3),
            Status = AttendanceStatus.ShortHours,
            ShortageMinutes = 120,
            CheckIn = Utc(2026, 8, 3).AddHours(8),
            CheckOut = Utc(2026, 8, 3).AddHours(14),
        });

        // Approved permission 14:00–16:00.
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp,
            Date = new DateTime(2026, 8, 3),
            FromMinutes = 840,
            ToMinutes = 960,
            ExcusedMinutes = 120,
            Source = AttendanceSources.AttendancePermission,
            RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var svc = Service(db);
        var rows = await svc.GetRangeRowsAsync(
            new AttendanceFilter { EmployeeId = emp },
            new DateTime(2026, 8, 3),
            new DateTime(2026, 8, 3),
            default);

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(0, row.ShortageMinutes);
        Assert.Equal(120, row.ExcusedMinutes);
        Assert.Equal(nameof(AttendanceStatus.Present), row.Status);
    }
}
