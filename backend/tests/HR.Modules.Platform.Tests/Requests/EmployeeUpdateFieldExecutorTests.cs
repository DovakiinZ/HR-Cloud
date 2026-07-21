using System.Text.Json;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Completion.Executors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

file sealed class FakeUser : ICurrentUserService
{
    public Guid UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid TenantId => Guid.Parse("22222222-2222-2222-2222-222222222222");
    public string? Email => "t@t";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

public class EmployeeUpdateFieldExecutorTests
{
    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options, new FakeUser());

    private static EffectContext Context(Guid employeeId, params (string Key, string? Value)[] payload)
    {
        var obj = new Dictionary<string, string?>();
        foreach (var (k, v) in payload) obj[k] = v;
        return new EffectContext
        {
            RequestInstanceId = Guid.NewGuid(),
            RequestNumber = "REQ-1",
            RequestTypeCode = "EMPLOYEE_DATA_UPDATE",
            EmployeeId = employeeId,
            ActorUserId = Guid.NewGuid(),
            Payload = JsonSerializer.SerializeToElement(obj),
        };
    }

    private static async Task<(ApplicationDbContext Db, Guid EmpId)> SeedEmployee(string name, string? phone = "0500000000")
    {
        var db = Ctx(name);
        var emp = new Employee { Id = Guid.NewGuid(), EmployeeNumber = "E1", FirstName = "A", LastName = "B", Email = "a@b.co", Phone = phone };
        db.Set<Employee>().Add(emp);
        await db.SaveChangesAsync();
        return (db, emp.Id);
    }

    [Fact]
    public async Task Updates_a_whitelisted_field_and_records_before_after()
    {
        var (db, empId) = await SeedEmployee(nameof(Updates_a_whitelisted_field_and_records_before_after));
        var exec = new EmployeeUpdateFieldExecutor(db);

        var res = await exec.ExecuteAsync(Context(empId, ("fieldKey", "phone"), ("newValue", "0555123456")), default);

        res.IsSkipped.Should().BeFalse();
        res.TargetEntityType.Should().Be("Employee");
        res.BeforeState.Should().NotBeNull();
        res.AfterState.Should().NotBeNull();
        (await db.Set<Employee>().FindAsync(empId))!.Phone.Should().Be("0555123456");
    }

    [Fact]
    public async Task Rejects_a_non_whitelisted_field()
    {
        var (db, empId) = await SeedEmployee(nameof(Rejects_a_non_whitelisted_field));
        var exec = new EmployeeUpdateFieldExecutor(db);

        var act = async () => await exec.ExecuteAsync(Context(empId, ("fieldKey", "basicSalary"), ("newValue", "99999")), default);

        await act.Should().ThrowAsync<InvalidOperationException>("an approved data-update must never reach a governed field");
        (await db.Set<Employee>().FindAsync(empId))!.BasicSalary.Should().Be(0);
    }

    [Fact]
    public async Task Skips_when_no_value_supplied_leaving_the_field_intact()
    {
        var (db, empId) = await SeedEmployee(nameof(Skips_when_no_value_supplied_leaving_the_field_intact), phone: "orig");
        var exec = new EmployeeUpdateFieldExecutor(db);

        var res = await exec.ExecuteAsync(Context(empId, ("fieldKey", "phone")), default);

        res.IsSkipped.Should().BeTrue();
        (await db.Set<Employee>().FindAsync(empId))!.Phone.Should().Be("orig");
    }

    [Fact]
    public void Whitelist_excludes_sensitive_fields()
    {
        EmployeeUpdateFieldExecutor.EditableFields.Should().Contain("phone");
        EmployeeUpdateFieldExecutor.EditableFields.Should().Contain("emergencyContactPhone");
        EmployeeUpdateFieldExecutor.EditableFields.Should().NotContain(new[] { "basicSalary", "iban", "bankId", "status", "email" });
    }
}
