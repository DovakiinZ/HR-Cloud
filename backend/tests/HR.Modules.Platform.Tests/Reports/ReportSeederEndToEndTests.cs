using System.Text;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance.Export;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Leave;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// End-to-end coverage for <see cref="ReportSeeder"/> and the display-resolution path, over a real
/// Postgres. Requires REPORTS_TEST_DB; skips cleanly without it.
///
/// Each test seeds its own tenant Guid and rolls back, so runs are isolated and repeatable.
/// The chain exercised is the real one: seeder → ObjectDefinition/ReportDefinition rows →
/// ReportExecutionService (catalog → SQL → ADO → shaper) → ReportExportService (CSV bytes).
/// </summary>
public class ReportSeederEndToEndTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private const string EmployeeFirstAr = "أحمد";
    private const string EmployeeLastAr = "الغامدي";
    private const string AnnualLeaveAr = "إجازة سنوية";

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed record Harness(
        ApplicationDbContext Db,
        TestUser User,
        ReportSeeder Seeder,
        ReportExecutionService Exec,
        ObjectCatalogService Catalog);

    private static Harness Build(Guid tenantId, ApplicationDbContext db)
    {
        var user = new TestUser(tenantId);
        var catalog = new ObjectCatalogService(db);
        var resolver = new ReportObjectResolver(db, catalog);
        var exec = new ReportExecutionService(db, user, resolver);
        var seeder = new ReportSeeder(db, user, catalog);
        return new Harness(db, user, seeder, exec, catalog);
    }

    private static ApplicationDbContext NewDb(TestUser user)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options, user);

    // ── 1. Idempotency ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Seeding_twice_creates_each_report_once()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var user = new TestUser(tenantId);
        await using var db = NewDb(user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var h = Build(tenantId, db);

        var first = await h.Seeder.SeedDefaultsAsync(default);
        var second = await h.Seeder.SeedDefaultsAsync(default);

        first.Should().NotBeEmpty();
        first.Should().OnlyContain(o => o.Status != ReportSeedStatus.AlreadyPresent,
            because: "nothing existed before the first pass");

        // The whole point: a restart must not duplicate anything.
        second.Should().OnlyContain(o => o.Status == ReportSeedStatus.AlreadyPresent
                                      || o.Status == ReportSeedStatus.Unsupported,
            because: "the second pass must recognise every report it already wrote");

        var codes = h.Seeder.AvailableCodes();
        var rows = await db.Set<ReportDefinition>().Where(r => codes.Contains(r.Code))
            .Select(r => r.Code).ToListAsync();
        rows.Should().OnlyHaveUniqueItems(because: "one definition per code, never a duplicate");

        // And the same must hold for the ObjectDefinition rows the reports point at.
        var objCodes = await db.Set<Domain.Engines.ObjectRegistry.ObjectDefinition>()
            .Select(o => o.Code).ToListAsync();
        objCodes.Should().OnlyHaveUniqueItems();

        await tx.RollbackAsync();
    }

    // ── 2-4. Display resolution: employee name, leave type label, enum label ──

    [SkippableFact]
    public async Task Leave_balance_report_shows_names_not_guids()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var user = new TestUser(tenantId);
        await using var db = NewDb(user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var h = Build(tenantId, db);

        var emp = MakeEmployee("E100");
        db.Set<Employee>().Add(emp);
        var leaveType = new MasterDataItem
        {
            Id = Guid.NewGuid(), ObjectType = MasterDataObjectType.LeaveType,
            Code = "ANNUAL", NameAr = AnnualLeaveAr, NameEn = "Annual Leave", IsActive = true,
        };
        db.Set<MasterDataItem>().Add(leaveType);
        await db.SaveChangesAsync();

        db.Set<LeaveBalance>().Add(new LeaveBalance
        {
            Id = Guid.NewGuid(), EmployeeId = emp.Id, LeaveTypeId = leaveType.Id,
            Year = 2026, EntitledDays = 30m, UsedDays = 11m, CarriedForwardDays = 4m,
        });
        await db.SaveChangesAsync();

        await h.Seeder.SeedDefaultsAsync(default);
        var report = await db.Set<ReportDefinition>().FirstAsync(r => r.Code == "leave-balance");

        var result = await h.Exec.RunAsync(report.Id, 1, 50, null, default);

        result.Rows.Should().NotBeEmpty(because: "one balance row was seeded for this tenant");
        var row = result.Rows[0];

        // The two columns that rendered as raw GUIDs in the reported bug.
        row["EmployeeId"]!.ToString().Should().Be($"{EmployeeFirstAr} {EmployeeLastAr}");
        row["LeaveTypeId"]!.ToString().Should().Be(AnnualLeaveAr);

        Guid.TryParse(row["EmployeeId"]!.ToString(), out _).Should().BeFalse();
        Guid.TryParse(row["LeaveTypeId"]!.ToString(), out _).Should().BeFalse();

        // Year must survive as a plain integer — the "2,026" formatting bug was downstream of this.
        row["Year"].Should().Be(2026);

        // RemainingDays via the existing CalculatedField path: 30 + 4 - 11 = 23.
        result.Columns.Should().Contain(c => c.Code == "RemainingDays");
        Convert.ToDouble(row["RemainingDays"]).Should().BeApproximately(23d, 0.001);

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Attendance_report_translates_enum_status_to_a_label()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var user = new TestUser(tenantId);
        await using var db = NewDb(user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var h = Build(tenantId, db);

        var emp = MakeEmployee("E200");
        db.Set<Employee>().Add(emp);
        await db.SaveChangesAsync();

        db.Set<AttendanceRecord>().Add(MakeAttendance(emp.Id, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            AttendanceStatus.Present, lateMinutes: 0));
        await db.SaveChangesAsync();

        await h.Seeder.SeedDefaultsAsync(default);
        var report = await db.Set<ReportDefinition>().FirstAsync(r => r.Code == "attendance-daily");

        var result = await h.Exec.RunAsync(report.Id, 1, 50, null, default);

        result.Rows.Should().NotBeEmpty();
        // Not the raw ordinal 1 — the enum name, resolved via Labels.EnumLabel.
        result.Rows[0]["Status"]!.ToString().Should().Be("Present");
        result.Rows[0]["EmployeeId"]!.ToString().Should().Be($"{EmployeeFirstAr} {EmployeeLastAr}");

        await tx.RollbackAsync();
    }

    // ── 5. Date range filters ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task Date_between_parameter_selects_only_the_requested_window()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var user = new TestUser(tenantId);
        await using var db = NewDb(user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var h = Build(tenantId, db);

        var emp = MakeEmployee("E300");
        db.Set<Employee>().Add(emp);
        await db.SaveChangesAsync();

        db.Set<AttendanceRecord>().AddRange(
            MakeAttendance(emp.Id, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), AttendanceStatus.Present, 0),
            MakeAttendance(emp.Id, new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), AttendanceStatus.Present, 0),
            MakeAttendance(emp.Id, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), AttendanceStatus.Present, 0));
        await db.SaveChangesAsync();

        await h.Seeder.SeedDefaultsAsync(default);
        var report = await db.Set<ReportDefinition>().FirstAsync(r => r.Code == "attendance-daily");

        // No parameters → the seeded wide default window → everything.
        var all = await h.Exec.RunAsync(report.Id, 1, 50, null, default);
        all.Rows.Should().HaveCount(3);

        // February only, via the ":to" upper-bound convention the viewer serializes.
        var feb = await h.Exec.RunAsync(report.Id, 1, 50, new Dictionary<string, string?>
        {
            ["Date"] = "2026-02-01",
            ["Date:to"] = "2026-02-28",
        }, default);
        feb.Rows.Should().HaveCount(1, because: "only the 15 Feb record falls inside the window");

        // Half-open range: a supplied lower bound with a blank upper must mean ">= from",
        // not "BETWEEN from AND NULL" (which would match nothing).
        var fromFeb = await h.Exec.RunAsync(report.Id, 1, 50, new Dictionary<string, string?>
        {
            ["Date"] = "2026-02-01",
            ["Date:to"] = "",
        }, default);
        fromFeb.Rows.Should().HaveCount(2, because: "February and March are on or after 1 Feb");

        // A blank optional parameter must mean "no constraint", never "= NULL".
        var blankEmployee = await h.Exec.RunAsync(report.Id, 1, 50, new Dictionary<string, string?>
        {
            ["EmployeeId"] = "",
        }, default);
        blankEmployee.Rows.Should().HaveCount(3, because: "an unfilled employee filter must not exclude every row");

        await tx.RollbackAsync();
    }

    // ── 6. Tenant isolation ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_tenant_never_sees_another_tenants_rows()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var userA = new TestUser(tenantA);
        await using var db = NewDb(userA);
        await using var tx = await db.Database.BeginTransactionAsync();

        // Tenant A's employee + attendance, written through A's context.
        var empA = MakeEmployee("A001");
        db.Set<Employee>().Add(empA);
        await db.SaveChangesAsync();
        db.Set<AttendanceRecord>().Add(MakeAttendance(empA.Id, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), AttendanceStatus.Present, 0));
        await db.SaveChangesAsync();

        var hA = Build(tenantA, db);
        await hA.Seeder.SeedDefaultsAsync(default);
        var reportA = await db.Set<ReportDefinition>().FirstAsync(r => r.Code == "attendance-daily");

        var resultA = await hA.Exec.RunAsync(reportA.Id, 1, 50, null, default);
        resultA.Rows.Should().HaveCount(1, because: "tenant A seeded exactly one attendance row");

        // Tenant B, same connection + same transaction, different tenant id on the user service.
        // B's report execution must not see A's row.
        var userB = new TestUser(tenantB);
        await using var dbB = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options, userB);
        await dbB.Database.UseTransactionAsync(tx.GetDbTransaction());

        var hB = Build(tenantB, dbB);
        await hB.Seeder.SeedDefaultsAsync(default);
        var reportB = await dbB.Set<ReportDefinition>().FirstAsync(r => r.Code == "attendance-daily");
        reportB.Id.Should().NotBe(reportA.Id, because: "each tenant gets its own definition row");

        var resultB = await hB.Exec.RunAsync(reportB.Id, 1, 50, null, default);
        resultB.Rows.Should().BeEmpty(because: "tenant B seeded no attendance rows and must not read tenant A's");

        await tx.RollbackAsync();
    }

    // ── 7. Export matches the grid ────────────────────────────────────────────

    [SkippableFact]
    public async Task Csv_export_carries_the_same_display_values_as_the_grid()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var user = new TestUser(tenantId);
        await using var db = NewDb(user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var h = Build(tenantId, db);

        var emp = MakeEmployee("E400");
        db.Set<Employee>().Add(emp);
        var leaveType = new MasterDataItem
        {
            Id = Guid.NewGuid(), ObjectType = MasterDataObjectType.LeaveType,
            Code = "ANNUAL", NameAr = AnnualLeaveAr, NameEn = "Annual Leave", IsActive = true,
        };
        db.Set<MasterDataItem>().Add(leaveType);
        await db.SaveChangesAsync();

        db.Set<LeaveBalance>().Add(new LeaveBalance
        {
            Id = Guid.NewGuid(), EmployeeId = emp.Id, LeaveTypeId = leaveType.Id,
            Year = 2026, EntitledDays = 30m, UsedDays = 11m, CarriedForwardDays = 4m,
        });
        await db.SaveChangesAsync();

        await h.Seeder.SeedDefaultsAsync(default);
        var report = await db.Set<ReportDefinition>().FirstAsync(r => r.Code == "leave-balance");

        var grid = await h.Exec.RunAsync(report.Id, 1, 50, null, default);

        var export = new ReportExportService(db, h.Exec, new AllowAllAccess(), new IExportWriter[] { new CsvExportWriter() });
        var file = await export.ExportAsync(report.Id, HR.Application.Engines.Finance.Export.ExportFormat.Csv, null, default);
        var csv = Encoding.UTF8.GetString(file.Content);

        // Every display value on screen must appear in the file — no GUID leaking into the export
        // because it took a different formatting path.
        csv.Should().Contain($"{EmployeeFirstAr} {EmployeeLastAr}");
        csv.Should().Contain(AnnualLeaveAr);
        csv.Should().NotContain(emp.Id.ToString());
        csv.Should().NotContain(leaveType.Id.ToString());

        var gridEmployee = grid.Rows[0]["EmployeeId"]!.ToString()!;
        csv.Should().Contain(gridEmployee, because: "the export and the grid must agree cell for cell");

        await tx.RollbackAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Employee MakeEmployee(string number) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = number,
        FirstName = "Ahmed",
        FirstNameAr = EmployeeFirstAr,
        LastName = "Alghamdi",
        LastNameAr = EmployeeLastAr,
        Email = $"{number}@test.example.com",
        Gender = Gender.Male,
        DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        BasicSalary = 10000m,
        Status = EmployeeStatus.Active,
    };

    private static AttendanceRecord MakeAttendance(Guid employeeId, DateTime date, AttendanceStatus status, int lateMinutes) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        Date = date,
        Status = status,
        LateMinutes = lateMinutes,
        WorkedMinutes = 480,
        RequiredMinutes = 480,
        Source = "Test",
    };

    private sealed class TestUser : ICurrentUserService
    {
        public TestUser(Guid tenantId) => TenantId = tenantId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; }
        public string? Email => "seeder-e2e@example.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    /// <summary>Access control is covered by ReportAccessServiceTests; this suite is about the data path.</summary>
    private sealed class AllowAllAccess : IReportAccessService
    {
        public Task EnsureCanReadAsync(Guid reportId, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureCanEditAsync(Guid reportId, CancellationToken ct) => Task.CompletedTask;
        public Task<IQueryable<ReportDefinition>> FilterVisibleAsync(IQueryable<ReportDefinition> source, CancellationToken ct)
            => Task.FromResult(source);
        public Task<ReportAccessContext> BuildContextAsync(CancellationToken ct)
            => throw new NotSupportedException("Not exercised by this suite.");
    }
}
