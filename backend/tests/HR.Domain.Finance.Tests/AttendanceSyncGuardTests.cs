using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
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

/// <summary>TDD tests for Task 6 (SP3): attendance sync routed through the shared
/// IPayrollPeriodGuard. SyncAsync must throw PayrollPeriodClosedException when called against
/// an immutable period; it must pass when the period is still mutable or no run exists.</summary>
public class AttendanceSyncGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId    => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId  => Tenant;
        public string? Email  => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        new FakeUser());

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    // Builds a sync service wired with the real PayrollPeriodGuard (same db).
    private static AttendancePayrollSyncService Svc(ApplicationDbContext db)
    {
        var calc  = new AttendanceWageCalculator(db);
        var facts = new PayrollFactProvider(db, null!, calc);
        return new AttendancePayrollSyncService(db, facts, calc, new PayrollPeriodGuard(db));
    }

    // ── seed helpers ──────────────────────────────────────────────────────────────

    /// <summary>Seeds a definition, version, employee (BasicSalary=3000), an Approved run with a
    /// population row, and one Absent attendance record in the period. Returns the version +
    /// employee Id so the caller can drive SyncAsync.</summary>
    private static async Task<(PayrollDefinitionVersion Version, Guid EmployeeId)>
        SeedApprovedRunWithAttendancePenaltyAsync(ApplicationDbContext db, int year, int month)
    {
        var def = new PayrollDefinition
        {
            TenantId = Tenant,
            Code     = $"MONTHLY-{Guid.NewGuid():N}",
            Name     = "Monthly",
            Status   = PayrollDefinitionStatus.Active,
        };
        db.PayrollDefinitions.Add(def);

        var ver = new PayrollDefinitionVersion
        {
            TenantId            = Tenant,
            PayrollDefinitionId = def.Id,
            VersionNumber       = 1,
            Status              = VersionStatus.Published,
            Frequency           = PayFrequency.Monthly,
            CutoffDay           = 27,
            CarryToNextPeriod   = false,
            DayBasis            = DayBasis.Fixed30,
            Currency            = "SAR",
            PublishedAt         = DateTime.UtcNow,
        };
        db.PayrollDefinitionVersions.Add(ver);
        def.CurrentVersionId = ver.Id;

        var emp = new Employee
        {
            TenantId       = Tenant,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali",
            LastName       = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            HireDate       = new DateTime(2024, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
            BasicSalary    = 3000m,
        };
        db.Employees.Add(emp);

        // Immutable (Approved) run for the target period.
        var run = new PayrollRun
        {
            TenantId                   = Tenant,
            RunNumber                  = $"RUN-{year}-{month:D2}-{Guid.NewGuid():N}",
            PayrollDefinitionId        = def.Id,
            PayrollDefinitionVersionId = ver.Id,
            TargetPeriodYear           = year,
            TargetPeriodMonth          = month,
            PeriodStart                = new DateTime(year, month, 1,  0, 0, 0, DateTimeKind.Utc),
            PeriodEnd                  = new DateTime(year, month, 27, 0, 0, 0, DateTimeKind.Utc),
            State                      = PayrollRunState.Approved, // immutable
        };
        db.PayrollRuns.Add(run);

        db.PayrollRunPopulations.Add(new PayrollRunPopulation
        {
            TenantId       = Tenant,
            PayrollRunId   = run.Id,
            EmployeeId     = emp.Id,
            EmployeeNumber = emp.EmployeeNumber,
            EmployeeName   = $"{emp.FirstName} {emp.LastName}",
            IsIncluded     = true,
        });

        // Master-data types required by the sync service.
        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
            db.MasterDataItems.Add(new MasterDataItem
            {
                ObjectType = MasterDataObjectType.DeductionType,
                Code = code, NameAr = code, NameEn = code,
            });

        // Attendance penalty (absence) inside the period to ensure amount > 0.
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = emp.Id,
            Date       = Utc(year, month, 2),
            Status     = AttendanceStatus.Absent,
        });

        await db.SaveChangesAsync();
        return (ver, emp.Id);
    }

    /// <summary>Same but run is Draft (mutable).</summary>
    private static async Task<(PayrollDefinitionVersion Version, Guid EmployeeId)>
        SeedDraftRunWithAttendancePenaltyAsync(ApplicationDbContext db, int year, int month)
    {
        var def = new PayrollDefinition
        {
            TenantId = Tenant,
            Code     = $"MONTHLY-{Guid.NewGuid():N}",
            Name     = "Monthly",
            Status   = PayrollDefinitionStatus.Active,
        };
        db.PayrollDefinitions.Add(def);

        var ver = new PayrollDefinitionVersion
        {
            TenantId            = Tenant,
            PayrollDefinitionId = def.Id,
            VersionNumber       = 1,
            Status              = VersionStatus.Published,
            Frequency           = PayFrequency.Monthly,
            CutoffDay           = 27,
            CarryToNextPeriod   = false,
            DayBasis            = DayBasis.Fixed30,
            Currency            = "SAR",
            PublishedAt         = DateTime.UtcNow,
        };
        db.PayrollDefinitionVersions.Add(ver);
        def.CurrentVersionId = ver.Id;

        var emp = new Employee
        {
            TenantId       = Tenant,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali",
            LastName       = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            HireDate       = new DateTime(2024, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
            BasicSalary    = 3000m,
        };
        db.Employees.Add(emp);

        var run = new PayrollRun
        {
            TenantId                   = Tenant,
            RunNumber                  = $"RUN-{year}-{month:D2}-{Guid.NewGuid():N}",
            PayrollDefinitionId        = def.Id,
            PayrollDefinitionVersionId = ver.Id,
            TargetPeriodYear           = year,
            TargetPeriodMonth          = month,
            PeriodStart                = new DateTime(year, month, 1,  0, 0, 0, DateTimeKind.Utc),
            PeriodEnd                  = new DateTime(year, month, 27, 0, 0, 0, DateTimeKind.Utc),
            State                      = PayrollRunState.Draft, // mutable
        };
        db.PayrollRuns.Add(run);

        db.PayrollRunPopulations.Add(new PayrollRunPopulation
        {
            TenantId       = Tenant,
            PayrollRunId   = run.Id,
            EmployeeId     = emp.Id,
            EmployeeNumber = emp.EmployeeNumber,
            EmployeeName   = $"{emp.FirstName} {emp.LastName}",
            IsIncluded     = true,
        });

        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
            db.MasterDataItems.Add(new MasterDataItem
            {
                ObjectType = MasterDataObjectType.DeductionType,
                Code = code, NameAr = code, NameEn = code,
            });

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = emp.Id,
            Date       = Utc(year, month, 2),
            Status     = AttendanceStatus.Absent,
        });

        await db.SaveChangesAsync();
        return (ver, emp.Id);
    }

    // ── tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_now_blocks_when_period_closed()
    {
        await using var ctx = Ctx($"sync-guard-block-{Guid.NewGuid()}");
        var (version, emp) = await SeedApprovedRunWithAttendancePenaltyAsync(ctx, 2026, 7);
        var sync = Svc(ctx);
        var period = new PayrollPeriod(Utc(2026, 7, 1), Utc(2026, 7, 31));

        var act = () => sync.SyncAsync(version, period, new[] { emp }, default);

        await act.Should().ThrowAsync<PayrollPeriodClosedException>();
    }

    [Fact]
    public async Task Sync_now_allows_when_run_is_mutable()
    {
        await using var ctx = Ctx($"sync-guard-allow-{Guid.NewGuid()}");
        var (version, emp) = await SeedDraftRunWithAttendancePenaltyAsync(ctx, 2026, 7);
        var sync = Svc(ctx);
        var period = new PayrollPeriod(Utc(2026, 7, 1), Utc(2026, 7, 31));

        var act = () => sync.SyncAsync(version, period, new[] { emp }, default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Sync_now_allows_when_no_run_exists_for_period()
    {
        await using var ctx = Ctx($"sync-guard-no-run-{Guid.NewGuid()}");

        // Seed employee + master-data + attendance but NO payroll run.
        var emp = new Employee
        {
            TenantId       = Tenant,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali", LastName = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            HireDate       = new DateTime(2024, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
            BasicSalary    = 3000m,
        };
        ctx.Employees.Add(emp);
        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
            ctx.MasterDataItems.Add(new MasterDataItem
            {
                ObjectType = MasterDataObjectType.DeductionType,
                Code = code, NameAr = code, NameEn = code,
            });
        ctx.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = emp.Id, Date = Utc(2026, 7, 2), Status = AttendanceStatus.Absent,
        });
        await ctx.SaveChangesAsync();

        var version = new PayrollDefinitionVersion
        {
            DayBasis = DayBasis.Fixed30, CutoffDay = 27, CarryToNextPeriod = false, Currency = "SAR",
        };
        var sync = Svc(ctx);
        var period = new PayrollPeriod(Utc(2026, 7, 1), Utc(2026, 7, 31));

        var act = () => sync.SyncAsync(version, period, new[] { emp.Id }, default);

        await act.Should().NotThrowAsync();
    }
}
