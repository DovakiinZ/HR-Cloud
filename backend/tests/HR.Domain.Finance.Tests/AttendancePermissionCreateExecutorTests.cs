using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Completion;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionCreateExecutorTests
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

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static AttendancePermissionCreateExecutor Executor(ApplicationDbContext db)
        => new(db, new ShiftResolver());

    /// <summary>Seed an Active employee assigned a fixed 08:00–16:00 day shift; returns the employee id.</summary>
    private static async Task<Guid> SeedEmployeeWithShiftAsync(ApplicationDbContext db)
    {
        var emp = new Employee
        {
            EmployeeNumber = $"E-PERM-{Guid.NewGuid():N}",
            FirstName = "Ali", LastName = "Test",
            Email = $"{Guid.NewGuid():N}@t.local",
            BasicSalary = 5000m,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var shift = new Shift
        {
            NameAr = "دوام صباحي", NameEn = "Day Shift",
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            RequiredMinutes = 480, BreakMinutes = 0, GraceAfterStartMinutes = 0,
            IsFlexible = false, IsActive = true, WeekendDays = "5,6",
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        db.ShiftAssignments.Add(new ShiftAssignment
        {
            ShiftId = shift.Id, EmployeeId = emp.Id,
            EffectiveFrom = Utc(2026, 1, 1), EffectiveTo = null,
            Priority = 10, IsActive = true,
        });
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private static EffectContext Context(Guid employeeId, Guid requestId, object payload) => new()
    {
        RequestInstanceId = requestId,
        RequestNumber = "REQ-1",
        RequestTypeCode = "ATTENDANCE_PERMISSION",
        EmployeeId = employeeId,
        ActorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact] // Records a durable excuse row; ExcusedMinutes = window∩shift (08:00–09:00 = 60 min).
    public async Task Creates_permission_row_with_window_shift_excused_minutes()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var reqId = Guid.NewGuid();

        var result = await Executor(db).ExecuteAsync(
            Context(emp, reqId, new { date = "2026-08-03", fromTime = "08:00", toTime = "09:00", reason = "طبيب" }), default);
        await db.SaveChangesAsync(); // the completion engine commits the transaction after the executor

        Assert.False(result.IsSkipped);
        var row = await db.AttendancePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(emp, row.EmployeeId);
        Assert.Equal(480, row.FromMinutes);
        Assert.Equal(540, row.ToMinutes);
        Assert.Equal(60, row.ExcusedMinutes);
        Assert.Equal(AttendanceSources.AttendancePermission, row.Source);
        Assert.Equal(reqId, row.RequestInstanceId);
        Assert.Equal("طبيب", row.Reason);
    }

    [Fact] // A window partly before the shift only counts the in-shift portion (07:00–09:00 → 60).
    public async Task Excused_minutes_clip_to_the_shift_span()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);

        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", fromTime = "07:00", toTime = "09:00" }), default);
        await db.SaveChangesAsync();

        var row = await db.AttendancePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(60, row.ExcusedMinutes); // only 08:00–09:00 lies within the shift
    }

    [Fact] // Re-running the same request is a no-op skip (one row, idempotent).
    public async Task Is_idempotent_per_request_instance()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var reqId = Guid.NewGuid();
        var payload = new { date = "2026-08-03", fromTime = "08:00", toTime = "09:00" };

        await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync(); // first approval commits
        var second = await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync();

        Assert.True(second.IsSkipped);
        Assert.Equal(1, await db.AttendancePermissions.CountAsync());
    }

    [Fact] // Over the monthly count cap under Block mode → throws, and no row is written.
    public async Task Blocks_and_writes_nothing_when_over_count_cap()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        db.AttendancePolicies.Add(new AttendancePolicy
        {
            IsActive = true, IsDefault = true,
            PermissionMaxPerMonth = 1, PermissionCapMode = PermissionCapMode.Block,
        });
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", fromTime = "08:00", toTime = "09:00" }), default));

        Assert.Equal(1, await db.AttendancePermissions.CountAsync()); // only the pre-existing one
    }

    [Fact] // Same breach under Warn mode still records the row (flagged, not blocked).
    public async Task Warns_but_writes_when_over_cap_in_warn_mode()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        db.AttendancePolicies.Add(new AttendancePolicy
        {
            IsActive = true, IsDefault = true,
            PermissionMaxPerMonth = 1, PermissionCapMode = PermissionCapMode.Warn,
        });
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var result = await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", fromTime = "08:00", toTime = "09:00" }), default);
        await db.SaveChangesAsync();

        Assert.False(result.IsSkipped);
        Assert.Equal(2, await db.AttendancePermissions.CountAsync());
    }
}
