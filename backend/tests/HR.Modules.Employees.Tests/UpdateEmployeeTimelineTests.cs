using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Audit;
using HR.Domain.Engines.Timeline;
using HR.Infrastructure.Engines.Timeline;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Commands;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Employees.Tests;

public class UpdateEmployeeTimelineTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = new[] { "Employees.Edit" };
        public bool IsAuthenticated => true;
    }

    private sealed class NoopAudit : IAuditEngine
    {
        public Task LogChange(string entityType, Guid entityId, string action,
            object? oldValues = null, object? newValues = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static ApplicationDbContext Ctx(ICurrentUserService user) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"emp-tl-{Guid.NewGuid()}").Options,
        user);

    [Fact]
    public async Task Updating_department_writes_an_Assignment_timeline_event()
    {
        var user = new FakeUser();
        await using var db = Ctx(user);
        var emp = new Employee
        {
            EmployeeNumber = "E1", FirstName = "A", LastName = "B", Email = "e1@t.local",
            DepartmentId = Guid.NewGuid(), BasicSalary = 5000m,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var handler = new UpdateEmployeeCommandHandler(db, new NoopAudit(),
            new TimelineProjectionService(new TimelineEngine(db, user)));

        await handler.Handle(new UpdateEmployeeCommand
        {
            Id = emp.Id,
            FirstName = "A",
            LastName = "B",
            Email = "e1@t.local",
            BasicSalary = 5000m,
            DepartmentId = Guid.NewGuid(), // changed department
        }, default);

        (await db.TimelineEvents.AnyAsync(t => t.EntityId == emp.Id
            && t.Category == nameof(TimelineCategory.Assignment))).Should().BeTrue();
    }
}
