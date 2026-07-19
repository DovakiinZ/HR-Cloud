using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
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

        // Both sides of the membership must exist first: user_roles has real FKs to users and
        // roles, so adding the join row alone fails on FK_user_roles_users_UserId /
        // FK_user_roles_roles_RoleId.
        var roleId = Guid.NewGuid();
        db.Set<Role>().Add(new Role { Id = roleId, Name = "ReportViewer", NameAr = "عارض التقارير" });
        db.Set<User>().Add(new User
        {
            Id = userId, Email = "caller@test.example.com", FullName = "Caller",
            PasswordHash = "x", IsActive = true,
        });
        await db.SaveChangesAsync();

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

    [SkippableFact]
    public async Task FilterVisible_excludes_foreign_personal_report()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new StubUser(userId, tenant);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(opts, user);
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code = "M"+Guid.NewGuid().ToString("N")[..6], NameEn="mine", NameAr="لي", OwnerId = userId, Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
        var foreign = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code = "F"+Guid.NewGuid().ToString("N")[..6], NameEn="foreign", NameAr="غريب", OwnerId = Guid.NewGuid(), Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
        db.Set<ReportDefinition>().AddRange(mine, foreign);
        await db.SaveChangesAsync();

        var svc = new ReportAccessService(db, user);
        var visible = await (await svc.FilterVisibleAsync(db.Set<ReportDefinition>(), default)).ToListAsync();

        visible.Select(r => r.Id).Should().Contain(mine.Id).And.NotContain(foreign.Id);
        await tx.RollbackAsync();
    }
}
