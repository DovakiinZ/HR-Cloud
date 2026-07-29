using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Application.Common.Paging;
using HR.Domain.Engines.Timeline;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Timeline;

public class EmployeeTimelineReadModelTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public FakeUser(params string[] perms) => Permissions = perms;
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; }
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(ICurrentUserService u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tl-read-{Guid.NewGuid()}").Options, u);

    private static RequestsController Controller(ApplicationDbContext db, ICurrentUserService u)
        => new(null!, db, u, null!, null!);

    private static async Task<Guid> SeedAsync(ApplicationDbContext db, params (string cat, string? meta)[] events)
    {
        var empId = Guid.NewGuid();
        foreach (var (cat, meta) in events)
            db.TimelineEvents.Add(new TimelineEvent
            {
                Category = cat,
                EntityType = "Employee",
                EntityId = empId,
                Action = $"{cat}Changed",
                DescriptionAr = "حدث",
                DescriptionEn = "event",
                Metadata = meta,
                OccurredAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
        return empId;
    }

    private static PagedResult<EmployeeTimelineDto> Page(
        ActionResult<ApiResponse<PagedResult<EmployeeTimelineDto>>> result)
        => ((result.Result as OkObjectResult)!.Value as ApiResponse<PagedResult<EmployeeTimelineDto>>)!.Data!;

    [Fact]
    public async Task Filters_by_category()
    {
        var u = new FakeUser();
        await using var db = Ctx(u);
        var empId = await SeedAsync(db, ("Assignment", null),
            ("Compensation", "{\"field\":\"BasicSalary\",\"before\":5000,\"after\":6000}"));

        var result = await Controller(db, u).EmployeeTimeline(empId, "Assignment", null, 1, 20, default);

        Page(result).Items.Should().OnlyContain(x => x.Category == "Assignment");
    }

    [Fact]
    public async Task Compensation_before_after_is_masked_without_ViewSensitive()
    {
        var u = new FakeUser(); // no permissions
        await using var db = Ctx(u);
        var empId = await SeedAsync(db,
            ("Compensation", "{\"field\":\"BasicSalary\",\"before\":5000,\"after\":6000}"));

        var result = await Controller(db, u).EmployeeTimeline(empId, null, null, 1, 20, default);
        var comp = Page(result).Items.Single(x => x.Category == "Compensation");
        comp.Before.Should().BeNull();
        comp.After.Should().BeNull();
    }

    [Fact]
    public async Task Compensation_before_after_visible_with_ViewSensitive()
    {
        var u = new FakeUser("Employees.ViewSensitive");
        await using var db = Ctx(u);
        var empId = await SeedAsync(db,
            ("Compensation", "{\"field\":\"BasicSalary\",\"before\":5000,\"after\":6000}"));

        var result = await Controller(db, u).EmployeeTimeline(empId, null, null, 1, 20, default);
        var comp = Page(result).Items.Single(x => x.Category == "Compensation");
        comp.After.Should().Be("6000");
    }
}
