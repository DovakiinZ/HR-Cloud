using System.Reflection;
using FluentAssertions;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>
/// Verifies that SeedData includes the Payroll.Transaction.CreateFromRun permission
/// (design-decision D10 / Task 16).
/// </summary>
public class CreateFromRunPermissionSeedTests
{
    private static readonly Guid ExpectedGuid = new("15f80127-6240-afe3-5c85-0bd941bf9c68");

    /// <summary>
    /// Retrieves the seeded Permission objects by calling SeedPermissions on a
    /// ModelBuilder and reading the internal _data list via reflection (HasData seeds
    /// are not applied in InMemory databases and GetSeedData() returns empty dicts
    /// until EF finalises the store model; reflection on _data is the stable path).
    /// </summary>
    private static IReadOnlyList<Permission> GetSeededPermissions()
    {
        var mb = new ModelBuilder();
        SeedData.SeedPermissions(mb);

        var entityType = mb.Model.FindEntityType(typeof(Permission))!;
        var dataField = entityType.GetType()
            .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var raw = (List<object>)dataField.GetValue(entityType)!;
        return raw.Cast<Permission>().ToList();
    }

    [Fact]
    public void SeedData_contains_PayrollTransaction_CreateFromRun_permission()
    {
        var permissions = GetSeededPermissions();

        permissions.Should().Contain(p =>
            p.Module == "Payroll.Transaction" && p.Name == "CreateFromRun",
            "SeedData must include the Payroll.Transaction.CreateFromRun permission");
    }

    [Fact]
    public void CreateFromRun_permission_has_deterministic_guid()
    {
        var permissions = GetSeededPermissions();

        var target = permissions.SingleOrDefault(p =>
            p.Module == "Payroll.Transaction" && p.Name == "CreateFromRun");

        target.Should().NotBeNull(
            "SeedData must contain Payroll.Transaction.CreateFromRun");
        target!.Id.Should().Be(ExpectedGuid,
            "deterministic MD5 of 'Payroll.Transaction.CreateFromRun' must equal {0}", ExpectedGuid);
    }
}
