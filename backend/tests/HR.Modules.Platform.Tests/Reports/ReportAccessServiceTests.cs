using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportAccessServiceTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private sealed class StubUser : ICurrentUserService
    {
        public StubUser(Guid u, Guid t) { UserId = u; TenantId = t; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string? Email => "t@e.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    [SkippableFact]
    public async Task Context_includes_caller_roles_and_department()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new StubUser(userId, tenant);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(opts, user);
        await using var tx = await db.Database.BeginTransactionAsync();

        var roleId = Guid.NewGuid();
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userId, RoleId = roleId });
        // Employee links this user to a department (Employee.UserId → DepartmentId).
        var deptId = Guid.NewGuid();
        db.Set<HR.Modules.Employees.Entities.Employee>().Add(new HR.Modules.Employees.Entities.Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "A1", FirstName = "T", LastName = "U",
            Email = "a1@e.com", Gender = Gender.Male,
            DateOfBirth = new DateTime(1990,1,1,0,0,0,DateTimeKind.Utc),
            HireDate = new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc),
            Status = EmployeeStatus.Active, UserId = userId, DepartmentId = deptId,
        });
        await db.SaveChangesAsync();

        var svc = new ReportAccessService(db, user);
        var ctx = await svc.BuildContextAsync(default);

        ctx.UserId.Should().Be(userId);
        ctx.RoleIds.Should().Contain(roleId);
        ctx.DepartmentId.Should().Be(deptId);

        await tx.RollbackAsync();
    }
}
