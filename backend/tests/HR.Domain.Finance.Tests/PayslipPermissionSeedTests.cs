using System.Reflection;
using FluentAssertions;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP4 Task 1 — the Payroll.Payslip.View/Print/Download permissions must be seeded so payslip
/// endpoints can be gated. Mirrors CreateFromRunPermissionSeedTests' reflection-over-_data approach.</summary>
public class PayslipPermissionSeedTests
{
    private static IReadOnlyList<Permission> GetSeededPermissions()
    {
        var mb = new ModelBuilder();
        SeedData.SeedPermissions(mb);
        var entityType = mb.Model.FindEntityType(typeof(Permission))!;
        var dataField = entityType.GetType().GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((List<object>)dataField.GetValue(entityType)!).Cast<Permission>().ToList();
    }

    [Theory]
    [InlineData("View")]
    [InlineData("Print")]
    [InlineData("Download")]
    public void SeedData_contains_Payroll_Payslip_permission(string name)
    {
        GetSeededPermissions().Should().Contain(p => p.Module == "Payroll.Payslip" && p.Name == name,
            "SeedData must include the Payroll.Payslip.{0} permission", name);
    }
}
