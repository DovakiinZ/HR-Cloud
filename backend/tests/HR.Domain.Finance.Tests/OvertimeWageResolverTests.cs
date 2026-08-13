using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Finance.Entities;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class OvertimeWageResolverTests
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

    private static async Task<Guid> SeedEmployeeAsync(ApplicationDbContext db, decimal basic)
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

    [Fact]
    public async Task HourlyWage_is_basic_over_30_over_8_and_default_multiplier_is_1_5()
    {
        await using var db = Ctx($"otw-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db, 7200m); // 7200/30/8 = 30/hr

        var (hourly, mult) = await new OvertimeWageResolver(db).ResolveAsync(emp, default);

        Assert.Equal(30m, hourly);
        Assert.Equal(1.5m, mult);
    }

    [Fact]
    public async Task Multiplier_reads_overtimeMultiplier_from_latest_published_version()
    {
        await using var db = Ctx($"otw-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db, 7200m);
        db.PayrollDefinitionVersions.Add(new PayrollDefinitionVersion
        {
            PayrollDefinitionId = Guid.NewGuid(),
            VersionNumber = 1,
            PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CalcSettingsJson = "{\"attendanceRates\":{\"overtimeMultiplier\":2.0}}",
        });
        await db.SaveChangesAsync();

        var (_, mult) = await new OvertimeWageResolver(db).ResolveAsync(emp, default);

        Assert.Equal(2.0m, mult);
    }
}
