using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Attendance;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Completion;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>Integration tests for the unpaid-permission → payroll-deduction path in
/// <see cref="AttendancePermissionCreateExecutor"/>.</summary>
public class AttendancePermissionPayrollTests
{
    // ── Fakes ──────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private sealed class AlwaysEligibleScopeEngine : IScopeEngine
    {
        public IReadOnlyList<ScopeDimensionInfo> Dimensions() => Array.Empty<ScopeDimensionInfo>();
        public Task<ScopeResolution> ResolveAsync(SelectionScope scope, CancellationToken ct)
            => Task.FromResult(new ScopeResolution(
                Array.Empty<Guid>(), Array.Empty<ScopeExclusion>(), Array.Empty<string>()));
    }

    /// <summary>Fake IPayrollPeriodGuard that never throws (period always open).</summary>
    private sealed class OpenPeriodGuard : IPayrollPeriodGuard
    {
        public Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Fake IPayrollPeriodGuard that always throws PayrollPeriodClosedException.</summary>
    private sealed class ClosedPeriodGuard : IPayrollPeriodGuard
    {
        public Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct = default)
            => throw new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
                ErrorCode: "PERIOD_CLOSED",
                BlockingRunId: Guid.NewGuid(),
                BlockingRunNumber: "RUN-2026-08",
                PayrollDefinitionId: Guid.NewGuid(),
                TargetPeriodYear: effectiveDate.Year,
                TargetPeriodMonth: effectiveDate.Month,
                BlockingRunState: "Approved"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Build executor with the supplied guard.</summary>
    private static AttendancePermissionCreateExecutor Executor(
        ApplicationDbContext db, IPayrollPeriodGuard? guard = null)
    {
        var types = new AttendancePermissionTypeService(db, new AlwaysEligibleScopeEngine());
        var resolver = new UnpaidPermissionWageResolver(db);
        return new(db, new ShiftResolver(), types, resolver, guard ?? new OpenPeriodGuard());
    }

    /// <summary>Seed employee + shift. Returns employee id.</summary>
    private static async Task<Guid> SeedEmployeeWithShiftAsync(
        ApplicationDbContext db, decimal basicSalary = 12000m)
    {
        var emp = new Employee
        {
            EmployeeNumber = $"E-PAY-{Guid.NewGuid():N}",
            FirstName = "Ahmad", LastName = "Test",
            Email = $"{Guid.NewGuid():N}@t.local",
            BasicSalary = basicSalary,
        };
        db.Employees.Add(emp);

        var shift = new Shift
        {
            NameAr = "دوام", NameEn = "Day",
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

    /// <summary>Seed the UNPAID_PERMISSION DeductionType. Returns its Id.</summary>
    private static async Task<Guid> SeedUnpaidDeductionTypeAsync(ApplicationDbContext db)
    {
        var item = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.DeductionType,
            Code = "UNPAID_PERMISSION",
            NameAr = "استئذان غير مدفوع",
            NameEn = "Unpaid Permission",
            IsActive = true,
        };
        db.MasterDataItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>Seed an AttendancePermissionType with Paid=<paramref name="paid"/>. Returns code.</summary>
    private static async Task<string> SeedPermissionTypeAsync(
        ApplicationDbContext db, bool paid)
    {
        var code = $"PERM-{Guid.NewGuid():N}";
        var rules = new PermissionTypeRules { Paid = paid };
        var item = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.AttendancePermissionType,
            Code = code,
            NameAr = paid ? "مدفوع" : "غير مدفوع",
            NameEn = paid ? "Paid" : "Unpaid",
            IsActive = true,
            MetadataJson = JsonSerializer.Serialize(rules),
        };
        db.MasterDataItems.Add(item);
        await db.SaveChangesAsync();
        return code;
    }

    /// <summary>Seed a default active AttendancePolicy.</summary>
    private static async Task SeedPolicyAsync(
        ApplicationDbContext db,
        DayBasis basis = DayBasis.Fixed30,
        decimal dailyHours = 8m,
        string? componentCodes = null)
    {
        db.AttendancePolicies.Add(new AttendancePolicy
        {
            IsActive = true, IsDefault = true,
            UnpaidDivisorBasis = basis,
            UnpaidDailyPayableHours = dailyHours,
            UnpaidWageComponentCodes = componentCodes,
        });
        await db.SaveChangesAsync();
    }

    private static EffectContext Context(Guid employeeId, Guid requestId, object payload) => new()
    {
        RequestInstanceId = requestId,
        RequestNumber = "REQ-PAY-1",
        RequestTypeCode = "ATTENDANCE_PERMISSION",
        EmployeeId = employeeId,
        ActorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paid_permission_creates_no_deduction()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        await SeedUnpaidDeductionTypeAsync(db);
        await SeedPolicyAsync(db);
        var code = await SeedPermissionTypeAsync(db, paid: true);

        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-04", fromTime = "08:00", toTime = "12:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.PayrollTransactions.CountAsync());
    }

    [Fact]
    public async Task Unpaid_permission_creates_deduction_equal_to_helper()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db, basicSalary: 12000m);
        await SeedUnpaidDeductionTypeAsync(db);
        await SeedPolicyAsync(db, basis: DayBasis.Fixed30, dailyHours: 8m);
        var code = await SeedPermissionTypeAsync(db, paid: false);

        // 08:00–12:00 = 240 min in-shift
        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-04", fromTime = "08:00", toTime = "12:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        var tx = await db.PayrollTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(PayrollTransactionKind.Deduction, tx.Kind);
        Assert.Equal(PayrollTransactionStatus.Approved, tx.Status);
        Assert.Equal("UnpaidPermission", tx.ReferenceType);
        Assert.NotEqual(Guid.Empty, tx.ReferenceId);
        Assert.Equal("AttendancePermission", tx.SourceModule);

        // amount = round((240/60) * (12000/30) / 8, 2) = round(4 * 400 / 8, 2) = 200
        var expectedAmount = UnpaidPermissionDeduction.Amount(12000m, 240, 30, 8m);
        Assert.Equal(expectedAmount, tx.Amount);
        Assert.Equal(200m, tx.Amount);

        // ReferenceId must point to the created permission row.
        var perm = await db.AttendancePermissions.AsNoTracking().SingleAsync();
        Assert.Equal(perm.Id, tx.ReferenceId);
    }

    [Fact]
    public async Task Unpaid_deduction_is_idempotent()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        await SeedUnpaidDeductionTypeAsync(db);
        await SeedPolicyAsync(db);
        var code = await SeedPermissionTypeAsync(db, paid: false);
        var reqId = Guid.NewGuid();
        var payload = new { date = "2026-08-04", fromTime = "08:00", toTime = "12:00", permissionTypeId = code };

        // First run — creates the permission row + deduction.
        await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync();

        // Second run — idempotency skip at executor level (AlreadyApplied).
        var second = await Executor(db).ExecuteAsync(Context(emp, reqId, payload), default);
        await db.SaveChangesAsync();

        Assert.True(second.IsSkipped);
        // Still exactly one deduction.
        Assert.Equal(1, await db.PayrollTransactions.CountAsync());
    }

    [Fact]
    public async Task Configurable_basis_is_honored()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db, basicSalary: 12000m);
        await SeedUnpaidDeductionTypeAsync(db);
        // Non-default policy: 26-day divisor, 7 payable hours.
        await SeedPolicyAsync(db, basis: DayBasis.Fixed30, dailyHours: 7m);

        // Manually override the basis via direct policy tweak (EF in-memory).
        var policy = await db.AttendancePolicies.SingleAsync();
        // We use fixed30 but set hours to 7 — change basis to WorkingDays to test divisor change.
        // Instead: seed a policy with divisor 26 (CalendarMonth for 2026-02 = 28, so use a fixed workaround:
        // we'll set divisorBasis=Fixed30 but tune hours, and separately test a month where CalendarMonth≠30).
        // SIMPLER: directly seed an explicit policy record with the desired values.
        db.AttendancePolicies.Remove(policy);
        await db.SaveChangesAsync();

        db.AttendancePolicies.Add(new AttendancePolicy
        {
            IsActive = true, IsDefault = true,
            UnpaidDivisorBasis = DayBasis.CalendarMonth, // August = 31 days
            UnpaidDailyPayableHours = 7m,
        });
        await db.SaveChangesAsync();

        var code = await SeedPermissionTypeAsync(db, paid: false);

        // 08:00–12:00 = 240 min in-shift (August 2026 = 31 calendar days)
        await Executor(db).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-04", fromTime = "08:00", toTime = "12:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        var tx = await db.PayrollTransactions.AsNoTracking().SingleAsync();

        // August has 31 days.
        var expected = UnpaidPermissionDeduction.Amount(12000m, 240, 31, 7m);
        Assert.Equal(expected, tx.Amount);

        // Verify it differs from the default (30 days / 8 hours).
        var defaultAmount = UnpaidPermissionDeduction.Amount(12000m, 240, 30, 8m);
        Assert.NotEqual(defaultAmount, tx.Amount);
    }

    [Fact]
    public async Task Finalized_period_flags_instead_of_deducting()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeWithShiftAsync(db);
        await SeedUnpaidDeductionTypeAsync(db);
        await SeedPolicyAsync(db);
        var code = await SeedPermissionTypeAsync(db, paid: false);

        // Use a guard that always throws PayrollPeriodClosedException.
        var closedGuard = new ClosedPeriodGuard();

        await Executor(db, closedGuard).ExecuteAsync(
            Context(emp, Guid.NewGuid(), new
            {
                date = "2026-08-04", fromTime = "08:00", toTime = "12:00",
                permissionTypeId = code
            }), default);
        await db.SaveChangesAsync();

        // No deduction created.
        Assert.Equal(0, await db.PayrollTransactions.CountAsync());

        // A PayrollAdjustmentNeeded notification must exist.
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal("PayrollAdjustmentNeeded", notification.Category);
        Assert.Equal("/payroll", notification.Link);
        Assert.False(string.IsNullOrWhiteSpace(notification.BodyAr));
        Assert.False(string.IsNullOrWhiteSpace(notification.BodyEn));
    }
}
