using System.Reflection;
using FluentAssertions;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP8 Task 1 — the Payroll.Audit.View permission gates the unified audit read.</summary>
public class AuditPermissionSeedTests
{
    private static IReadOnlyList<Permission> Seeded()
    {
        var mb = new ModelBuilder();
        SeedData.SeedPermissions(mb);
        var et = mb.Model.FindEntityType(typeof(Permission))!;
        var data = (List<object>)et.GetType().GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(et)!;
        return data.Cast<Permission>().ToList();
    }

    [Fact]
    public void SeedData_contains_Payroll_Audit_View()
        => Seeded().Should().Contain(p => p.Module == "Payroll.Audit" && p.Name == "View");
}
