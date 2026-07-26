using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Identity.Entities;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class NotificationRecipientResolverTests
{
    private sealed class FakeUser : HR.Application.Common.Interfaces.ICurrentUserService
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public Guid TenantId { get; init; } = Guid.NewGuid();
        public string? Email => "a@b.c";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Db(FakeUser u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"rr_{Guid.NewGuid()}").Options, u);

    private static Employee Emp(Guid tenant, Guid? userId = null, Guid? managerId = null, Guid? deptId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, EmployeeNumber = $"E{Guid.NewGuid():N}".Substring(0, 8),
        FirstName = "F", LastName = "L", Email = "e@e.e", Gender = Gender.Male,
        DateOfBirth = new DateTime(1990, 1, 1), HireDate = new DateTime(2020, 1, 1),
        UserId = userId, ManagerId = managerId, DepartmentId = deptId,
    };

    private static RequestInstance Inst(Guid tenant, Guid empId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, RequestTypeId = Guid.NewGuid(), RequestNumber = "REQ-1",
        EmployeeId = empId, FormSubmissionId = Guid.NewGuid(), Status = RequestStatus.InProgress,
        SubmittedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Requester_resolves_to_employee_user()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var reqUser = Guid.NewGuid();
        var emp = Emp(u.TenantId, userId: reqUser);
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.Requester), inst, null, default);
        r.Should().ContainSingle().Which.Should().Be(reqUser);
    }

    [Fact]
    public async Task DirectManager_resolves_to_manager_user()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var mgrUser = Guid.NewGuid();
        var mgr = Emp(u.TenantId, userId: mgrUser);
        var emp = Emp(u.TenantId, userId: Guid.NewGuid(), managerId: mgr.Id);
        db.Set<Employee>().AddRange(mgr, emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.DirectManager), inst, null, default);
        r.Should().ContainSingle().Which.Should().Be(mgrUser);
    }

    [Fact]
    public async Task DirectManager_with_no_manager_resolves_empty()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var emp = Emp(u.TenantId, userId: Guid.NewGuid()); // no managerId
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.DirectManager), inst, null, default);
        r.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentApprover_resolves_from_step()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var emp = Emp(u.TenantId, userId: Guid.NewGuid());
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();
        var approverUser = Guid.NewGuid();
        var step = new RequestApproval { Id = Guid.NewGuid(), RequestInstanceId = inst.Id, StepOrder = 1,
            StepNameAr = "1", StepNameEn = "1", ApproverType = ApproverType.DirectManager,
            AssignedToUserId = approverUser, Status = RequestApprovalStatus.Pending };

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.CurrentApprover), inst, step, default);
        r.Should().ContainSingle().Which.Should().Be(approverUser);
    }

    [Fact]
    public async Task Role_resolves_all_active_members()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var roleId = Guid.NewGuid();
        var u1 = new User { Id = Guid.NewGuid(), TenantId = u.TenantId, Email = "u1@x.c", PasswordHash = "x", FullName = "U1", IsActive = true };
        var u2 = new User { Id = Guid.NewGuid(), TenantId = u.TenantId, Email = "u2@x.c", PasswordHash = "x", FullName = "U2", IsActive = true };
        db.Set<User>().AddRange(u1, u2);
        db.Set<UserRole>().AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = u1.Id, RoleId = roleId },
            new UserRole { Id = Guid.NewGuid(), UserId = u2.Id, RoleId = roleId });
        var emp = Emp(u.TenantId, userId: Guid.NewGuid());
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.Role, roleId), inst, null, default);
        r.Should().BeEquivalentTo(new[] { u1.Id, u2.Id });
    }
}
