using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Attendance;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.MasterData;
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
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    /// <summary>Scope engine that always returns all employees (no filtering) — OK for types
    /// whose Eligibility is null or Mode=All.</summary>
    private sealed class AlwaysEligibleScopeEngine : IScopeEngine
    {
        public IReadOnlyList<ScopeDimensionInfo> Dimensions() => Array.Empty<ScopeDimensionInfo>();
        public Task<ScopeResolution> ResolveAsync(SelectionScope scope, CancellationToken ct)
            => Task.FromResult(new ScopeResolution(
                Array.Empty<Guid>(), Array.Empty<ScopeExclusion>(), Array.Empty<string>()));
    }

    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>No-throw period guard — existing tests don't need payroll-period behavior.</summary>
    private sealed class OpenPeriodGuard : IPayrollPeriodGuard
    {
        public Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static AttendancePermissionCreateExecutor Executor(ApplicationDbContext db)
    {
        var types = new AttendancePermissionTypeService(db, new AlwaysEligibleScopeEngine());
        var resolver = new UnpaidPermissionWageResolver(db);
        return new(db, new ShiftResolver(), types, resolver, new OpenPeriodGuard());
    }

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

    /// <summary>Seed an AttendancePermissionType MasterDataItem with the given rules; returns its Id and Code.</summary>
    private static async Task<(Guid Id, string Code)> SeedPermissionTypeAsync(
        ApplicationDbContext db,
        int? maxRequestsPerMonth = null,
        int? maxMinutesPerMonth = null,
        int? maxMinutesPerRequest = null,
        int? maxRequestsPerDay = null,
        int? maxMinutesPerDay = null,
        PermissionExceedBehavior behavior = PermissionExceedBehavior.Block)
    {
        var code = $"PERM-{Guid.NewGuid():N}";
        var rules = new PermissionTypeRules
        {
            MaxRequestsPerMonth = maxRequestsPerMonth,
            MaxMinutesPerMonth = maxMinutesPerMonth,
            MaxMinutesPerRequest = maxMinutesPerRequest,
            MaxRequestsPerDay = maxRequestsPerDay,
            MaxMinutesPerDay = maxMinutesPerDay,
            ExceedBehavior = behavior,
        };
        var item = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.AttendancePermissionType,
            Code = code,
            NameAr = "استئذان اختباري",
            NameEn = "Test Permission Type",
            IsActive = true,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(rules),
        };
        db.MasterDataItems.Add(item);
        await db.SaveChangesAsync();
        return (item.Id, code);
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
        var (_, code) = await SeedPermissionTypeAsync(db);
        var reqId = Guid.NewGuid();

        var result = await Executor(db).ExecuteAsync(
            Context(emp, reqId, new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                reason = "طبيب", permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

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

    [Fact] // PermissionTypeId is stamped on the created row.
    public async Task Stamps_PermissionTypeId_on_created_row()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var (typeId, code) = await SeedPermissionTypeAsync(db);

        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        var row = await db.AttendancePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(typeId, row.PermissionTypeId);
    }

    [Fact] // A window partly before the shift only counts the in-shift portion (07:00–09:00 → 60).
    public async Task Excused_minutes_clip_to_the_shift_span()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var (_, code) = await SeedPermissionTypeAsync(db);

        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "07:00", toTime = "09:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        var row = await db.AttendancePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(60, row.ExcusedMinutes); // only 08:00–09:00 lies within the shift
    }

    [Fact] // Re-running the same request is a no-op skip (one row, idempotent).
    public async Task Is_idempotent_per_request_instance()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var (_, code) = await SeedPermissionTypeAsync(db);
        var reqId = Guid.NewGuid();
        var payload = new { date = "2026-08-03", fromTime = "08:00", toTime = "09:00", permissionTypeId = code };

        await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync(); // first approval commits
        var second = await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync();

        Assert.True(second.IsSkipped);
        Assert.Equal(1, await db.AttendancePermissions.CountAsync());
    }

    [Fact] // Missing permissionTypeId → throws NonRetryable.
    public async Task Throws_when_permissionTypeId_missing()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);

        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new { date = "2026-08-03", fromTime = "08:00", toTime = "09:00" }),
            default));
    }

    [Fact] // Unknown / inactive permissionTypeId → throws NonRetryable.
    public async Task Throws_when_permissionType_not_found()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);

        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = "NONEXISTENT-TYPE"
            }), default));
    }

    [Fact] // Over monthly count cap (Block) → throws, and no row is written.
    public async Task Blocks_and_writes_nothing_when_over_count_cap()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        // maxRequestsPerMonth=1, Block
        var (typeId, code) = await SeedPermissionTypeAsync(db, maxRequestsPerMonth: 1, behavior: PermissionExceedBehavior.Block);

        // Seed one already-used permission for this type.
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, PermissionTypeId = typeId,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = code
            }), default));

        Assert.Equal(1, await db.AttendancePermissions.CountAsync()); // only the pre-existing one
    }

    [Fact] // Same breach under Warn mode still records the row (flagged, not blocked).
    public async Task Warns_but_writes_when_over_cap_in_warn_mode()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var (typeId, code) = await SeedPermissionTypeAsync(db, maxRequestsPerMonth: 1, behavior: PermissionExceedBehavior.Warn);

        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, PermissionTypeId = typeId,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var result = await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        Assert.False(result.IsSkipped);
        Assert.Equal(2, await db.AttendancePermissions.CountAsync());
    }

    [Fact] // RequireApprovalOverride + missing overrideReason → throws.
    public async Task Requires_override_reason_when_behavior_is_override_and_reason_missing()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var (typeId, code) = await SeedPermissionTypeAsync(db, maxRequestsPerMonth: 1, behavior: PermissionExceedBehavior.RequireApprovalOverride);

        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, PermissionTypeId = typeId,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        // No overrideReason → throws.
        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = code,
                // overrideReason intentionally omitted
            }), default));
    }

    [Fact] // RequireApprovalOverride + present overrideReason → writes row + audit log, capOverride=true.
    public async Task Requires_override_reason_when_behavior_is_override_and_reason_present()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        var (typeId, code) = await SeedPermissionTypeAsync(db, maxRequestsPerMonth: 1, behavior: PermissionExceedBehavior.RequireApprovalOverride);

        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, PermissionTypeId = typeId,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var result = await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = code,
                overrideReason = "مدير وافق / Manager approved exception",
            }), default);
        await db.SaveChangesAsync();

        Assert.False(result.IsSkipped);
        // New permission row written.
        Assert.Equal(2, await db.AttendancePermissions.CountAsync());
        // Audit log written.
        var audit = await db.AttendanceAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("PermissionCapOverride", audit.Action);
        Assert.False(string.IsNullOrWhiteSpace(audit.DetailsEn));
        Assert.False(string.IsNullOrWhiteSpace(audit.DetailsAr));
        // capOverride flagged in after-state.
        var afterJson = System.Text.Json.JsonSerializer.Serialize(result.AfterState);
        Assert.Contains("capOverride", afterJson);
    }

    [Fact] // Policy-level monthly dims are respected when type has no explicit month limits.
    public async Task Respects_policy_monthly_fallback_when_type_has_no_month_limits()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);

        // Seed an unlimited type (no type-level limits).
        var (typeId, code) = await SeedPermissionTypeAsync(db, behavior: PermissionExceedBehavior.Block);

        // Policy sets monthly count = 1.
        db.AttendancePolicies.Add(new AttendancePolicy
        {
            IsActive = true, IsDefault = true,
            PermissionMaxPerMonth = 1, PermissionCapMode = PermissionCapMode.Block,
        });

        // Pre-existing permission for this type this month.
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = Utc(2026, 8, 1), FromMinutes = 480, ToMinutes = 540,
            ExcusedMinutes = 60, PermissionTypeId = typeId,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NonRetryableEffectException>(() => Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-03", fromTime = "08:00", toTime = "09:00",
                permissionTypeId = code
            }), default));
    }
}
