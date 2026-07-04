using System.Reflection;
using FluentAssertions;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP7 — the consolidated payroll RBAC surface. A single guard that documents and enforces the
/// complete set of payroll permissions seeded across SP1–SP9, so a future change can't silently drop one.</summary>
public class PayrollPermissionCatalogTests
{
    /// <summary>Every payroll-related (module, name) permission the platform must seed.</summary>
    public static readonly (string Module, string Name)[] Expected =
    {
        ("Payroll", "View"), ("Payroll", "Create"), ("Payroll", "Edit"), ("Payroll", "Delete"),
        ("Payroll", "Approve"), ("Payroll", "Export"), ("Payroll", "Run"), ("Payroll", "Lock"), ("Payroll", "Configure"),
        ("Payroll.Transaction", "CreateFromRun"),
        ("Payroll.Payslip", "View"), ("Payroll.Payslip", "Print"), ("Payroll.Payslip", "Download"),
        ("Payroll.Export", "Bank"),
        ("Payroll.Run", "Void"), ("Payroll.Run", "Amend"), ("Payroll.Run", "Reissue"),
        ("Payroll.Audit", "View"),
        ("Attendance.PayrollImpact", "Create"),
    };

    private static IReadOnlyList<Permission> Seeded()
    {
        var mb = new ModelBuilder();
        SeedData.SeedPermissions(mb);
        var et = mb.Model.FindEntityType(typeof(Permission))!;
        var data = (List<object>)et.GetType().GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(et)!;
        return data.Cast<Permission>().ToList();
    }

    [Fact]
    public void All_payroll_permissions_are_seeded()
    {
        var seeded = Seeded().Select(p => (p.Module, p.Name)).ToHashSet();
        var missing = Expected.Where(e => !seeded.Contains(e)).ToList();
        missing.Should().BeEmpty("every payroll permission in the catalog must be seeded; missing: {0}",
            string.Join(", ", missing.Select(m => $"{m.Module}.{m.Name}")));
    }

    [Fact]
    public void Payroll_permission_ids_are_deterministic_and_unique()
    {
        var ids = Seeded()
            .Where(p => p.Module.StartsWith("Payroll") || p.Module == "Attendance.PayrollImpact")
            .Select(p => p.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(System.Guid.Empty);
    }
}
