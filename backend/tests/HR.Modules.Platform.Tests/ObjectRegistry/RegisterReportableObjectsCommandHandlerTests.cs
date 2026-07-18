using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.ObjectRegistry;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.ObjectRegistry;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.ObjectRegistry;

// ── Fakes ────────────────────────────────────────────────────────────────────

file sealed class FakeUser : ICurrentUserService
{
    public static readonly Guid TestTenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public Guid UserId           => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public Guid TenantId         => TestTenantId;
    public string? Email         => "test@local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated  => true;
}

file sealed class FakeObjectCatalogService : IObjectCatalogService
{
    private static readonly string[] KnownCodes =
        { "Employee", "AttendanceRecord", "PayrollPayslip", "LeaveBalance", "RequestInstance" };

    public IReadOnlyList<CatalogObjectDto> GetCatalog()
        => KnownCodes.Select(MakeDto).ToList();

    public CatalogObjectDto? GetObject(string objectCode)
        => KnownCodes.Any(c => string.Equals(c, objectCode, StringComparison.OrdinalIgnoreCase))
            ? MakeDto(objectCode)
            : null;

    public ResolvedObject? Resolve(string objectCode)
        => KnownCodes.Any(c => string.Equals(c, objectCode, StringComparison.OrdinalIgnoreCase))
            ? new ResolvedObject
              {
                  Code = objectCode,
                  TableName = TableFor(objectCode),
                  HasTenant = true,
                  HasSoftDelete = false,
                  KeyColumn = "Id",
                  Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase),
              }
            : null;

    private static CatalogObjectDto MakeDto(string code) => new()
    {
        Code    = code,
        NameEn  = $"{code} (EN)",
        NameAr  = $"{code} (AR)",
        Module  = ModuleFor(code),
        Fields  = new List<CatalogFieldDto>(),
    };

    private static string TableFor(string code) => code switch
    {
        "Employee"         => "Employees",
        "AttendanceRecord" => "AttendanceRecords",
        "PayrollPayslip"   => "PayrollPayslips",
        "LeaveBalance"     => "LeaveBalances",
        "RequestInstance"  => "RequestInstances",
        _                  => code + "s",
    };

    private static string ModuleFor(string code) => code switch
    {
        "Employee"         => "Employees",
        "AttendanceRecord" => "Attendance",
        "PayrollPayslip"   => "Payroll",
        "LeaveBalance"     => "Leave",
        "RequestInstance"  => "Requests",
        _                  => "General",
    };
}

/// <summary>
/// Catalog stub that can't resolve any code (simulates missing catalog entries).
/// </summary>
file sealed class EmptyObjectCatalogService : IObjectCatalogService
{
    public IReadOnlyList<CatalogObjectDto> GetCatalog() => Array.Empty<CatalogObjectDto>();
    public CatalogObjectDto? GetObject(string objectCode) => null;
    public ResolvedObject? Resolve(string objectCode)    => null;
}

/// <summary>
/// Catalog stub where only some codes resolve (simulates partial catalog).
/// </summary>
file sealed class PartialObjectCatalogService : IObjectCatalogService
{
    private readonly HashSet<string> _resolvable;

    public PartialObjectCatalogService(params string[] resolvable)
        => _resolvable = new HashSet<string>(resolvable, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CatalogObjectDto> GetCatalog()
        => _resolvable.Select(c => new CatalogObjectDto
           {
               Code = c, NameEn = c + " EN", NameAr = c + " AR", Module = "Test", Fields = new(),
           }).ToList();

    public CatalogObjectDto? GetObject(string objectCode)
        => _resolvable.Contains(objectCode)
            ? new CatalogObjectDto { Code = objectCode, NameEn = objectCode + " EN", NameAr = objectCode + " AR", Module = "Test", Fields = new() }
            : null;

    public ResolvedObject? Resolve(string objectCode)
        => _resolvable.Contains(objectCode)
            ? new ResolvedObject
              {
                  Code = objectCode, TableName = objectCode + "s", HasTenant = true,
                  HasSoftDelete = false, KeyColumn = "Id",
                  Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase),
              }
            : null;
}

// ── Test Fixture ─────────────────────────────────────────────────────────────

public class RegisterReportableObjectsCommandHandlerTests
{
    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options,
        new FakeUser());

    // ── (a) creates the 5 objects when none exist ─────────────────────────────

    [Fact]
    public async Task Creates_all_resolvable_objects_when_none_exist()
    {
        await using var db = Ctx(nameof(Creates_all_resolvable_objects_when_none_exist));
        var catalog = new FakeObjectCatalogService();
        var handler = new RegisterReportableObjectsCommandHandler(
            db, catalog, NullLogger<RegisterReportableObjectsCommandHandler>.Instance);

        var count = await handler.Handle(new RegisterReportableObjectsCommand(), CancellationToken.None);

        count.Should().Be(5);

        var rows = await db.ObjectDefinitions.IgnoreQueryFilters().ToListAsync();
        rows.Should().HaveCount(5);

        var codes = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        codes.Should().Contain("Employee");
        codes.Should().Contain("AttendanceRecord");
        codes.Should().Contain("PayrollPayslip");
        codes.Should().Contain("LeaveBalance");
        codes.Should().Contain("RequestInstance");
    }

    // ── (b) idempotent — second call returns 0, no duplicates ─────────────────

    [Fact]
    public async Task Second_call_is_idempotent_returns_zero_no_duplicates()
    {
        await using var db = Ctx(nameof(Second_call_is_idempotent_returns_zero_no_duplicates));
        var catalog = new FakeObjectCatalogService();
        var handler = new RegisterReportableObjectsCommandHandler(
            db, catalog, NullLogger<RegisterReportableObjectsCommandHandler>.Instance);

        var firstCount = await handler.Handle(new RegisterReportableObjectsCommand(), CancellationToken.None);
        var secondCount = await handler.Handle(new RegisterReportableObjectsCommand(), CancellationToken.None);

        firstCount.Should().Be(5);
        secondCount.Should().Be(0, "all objects already registered — nothing to add");

        var rows = await db.ObjectDefinitions.IgnoreQueryFilters().ToListAsync();
        rows.Should().HaveCount(5, "no duplicates created on second call");
    }

    // ── (c) unresolvable code is skipped; resolvable ones still register ───────

    [Fact]
    public async Task Unresolvable_code_skipped_others_still_registered()
    {
        await using var db = Ctx(nameof(Unresolvable_code_skipped_others_still_registered));

        // Only 3 of the 5 canonical codes exist in this catalog
        var catalog = new PartialObjectCatalogService("Employee", "LeaveBalance", "RequestInstance");
        var handler = new RegisterReportableObjectsCommandHandler(
            db, catalog, NullLogger<RegisterReportableObjectsCommandHandler>.Instance);

        var count = await handler.Handle(new RegisterReportableObjectsCommand(), CancellationToken.None);

        count.Should().Be(3);

        var rows = await db.ObjectDefinitions.IgnoreQueryFilters().ToListAsync();
        rows.Should().HaveCount(3);

        var codes = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        codes.Should().Contain("Employee");
        codes.Should().Contain("LeaveBalance");
        codes.Should().Contain("RequestInstance");
        codes.Should().NotContain("AttendanceRecord");
        codes.Should().NotContain("PayrollPayslip");
    }

    // ── (d) created rows have IsSystem=true + correct TableName ───────────────

    [Fact]
    public async Task Created_rows_have_IsSystem_true_and_catalog_TableName()
    {
        await using var db = Ctx(nameof(Created_rows_have_IsSystem_true_and_catalog_TableName));
        var catalog = new FakeObjectCatalogService();
        var handler = new RegisterReportableObjectsCommandHandler(
            db, catalog, NullLogger<RegisterReportableObjectsCommandHandler>.Instance);

        await handler.Handle(new RegisterReportableObjectsCommand(), CancellationToken.None);

        var rows = await db.ObjectDefinitions.IgnoreQueryFilters().ToListAsync();

        rows.Should().AllSatisfy(r =>
        {
            r.IsSystem.Should().BeTrue();
            r.IsActive.Should().BeTrue();
            r.TableName.Should().NotBeNullOrWhiteSpace();
            r.TenantId.Should().Be(FakeUser.TestTenantId);
        });

        var employee = rows.Single(r => string.Equals(r.Code, "Employee", StringComparison.OrdinalIgnoreCase));
        employee.TableName.Should().Be("Employees");

        var attendance = rows.Single(r => string.Equals(r.Code, "AttendanceRecord", StringComparison.OrdinalIgnoreCase));
        attendance.TableName.Should().Be("AttendanceRecords");
    }

    // ── (e) empty catalog → zero objects, no exception ────────────────────────

    [Fact]
    public async Task Empty_catalog_returns_zero_without_throwing()
    {
        await using var db = Ctx(nameof(Empty_catalog_returns_zero_without_throwing));
        var catalog = new EmptyObjectCatalogService();
        var handler = new RegisterReportableObjectsCommandHandler(
            db, catalog, NullLogger<RegisterReportableObjectsCommandHandler>.Instance);

        // Should not throw — invoke directly and assert no exception
        var count = await handler.Handle(new RegisterReportableObjectsCommand(), CancellationToken.None);
        count.Should().Be(0);

        var rows = await db.ObjectDefinitions.IgnoreQueryFilters().ToListAsync();
        rows.Should().BeEmpty();
    }
}
