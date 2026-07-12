using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.ObjectRegistry;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// End-to-end integration test for <see cref="ReportExecutionService"/>.
/// Requires a Postgres connection string in the REPORTS_TEST_DB environment variable.
/// Skips cleanly when the variable is absent so CI unit-test runs are unaffected.
/// </summary>
public class ReportExecutionIntegrationTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    [SkippableFact]
    public async Task Runs_employee_by_department_report_with_sum()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run this integration test.");

        // ── Arrange ──────────────────────────────────────────────────────────────

        var tenantId = Guid.NewGuid();
        var userService = new IntegrationTestUserService(tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(Conn)
            .Options;

        await using var db = new ApplicationDbContext(options, userService);

        // Use an ambient transaction that we roll back after the test.
        await using var tx = await db.Database.BeginTransactionAsync();

        // Seed ObjectDefinition records for Employee and Department.
        // The catalog discovers objects via EF model metadata (code = entity short-name),
        // so we create matching ObjectDefinition rows that ReportObjectResolver will
        // look up by Guid.
        var empObjDef = new ObjectDefinition
        {
            Id = Guid.NewGuid(),
            Code = "Employee",
            NameEn = "Employee",
            NameAr = "موظف",
            TableName = "Employees",
            IsActive = true,
        };
        var deptObjDef = new ObjectDefinition
        {
            Id = Guid.NewGuid(),
            Code = "Department",
            NameEn = "Department",
            NameAr = "قسم",
            TableName = "Departments",
            IsActive = true,
        };
        db.Set<ObjectDefinition>().AddRange(empObjDef, deptObjDef);
        await db.SaveChangesAsync();

        // Seed a ReportDefinition: primary=Employee, join Department,
        // fields: Department.Name (dimension) + Employee.BasicSalary (Sum aggregate),
        // grouping: Department.Name.
        var reportId = Guid.NewGuid();
        var report = new ReportDefinition
        {
            Id = reportId,
            TenantId = tenantId,
            Code = "TEST_EMP_BY_DEPT_" + reportId.ToString("N")[..8],
            NameEn = "Employees by Department (Integration Test)",
            NameAr = "الموظفون حسب القسم",
            PrimaryObjectId = empObjDef.Id,
            IsPublished = true,
            IsActive = true,
        };

        // Department.Name field (dimension from joined object)
        var deptNameField = new ReportField
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            FieldType = ReportFieldType.ObjectField,
            ObjectDefinitionId = deptObjDef.Id,
            FieldCode = "Name",
            DisplayNameEn = "Department Name",
            DisplayNameAr = "اسم القسم",
            SortOrder = 1,
            IsVisible = true,
        };

        // Employee.BasicSalary field (measure, Sum aggregate)
        var salaryField = new ReportField
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            FieldType = ReportFieldType.ObjectField,
            ObjectDefinitionId = empObjDef.Id,
            FieldCode = "BasicSalary",
            DisplayNameEn = "Basic Salary",
            DisplayNameAr = "الراتب الأساسي",
            Aggregation = AggregationType.Sum,
            SortOrder = 2,
            IsVisible = true,
        };

        // Relationship: Employee (source) → Department (target) via DepartmentId FK
        var relationship = new ReportRelationship
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            SourceObjectId = empObjDef.Id,
            TargetObjectId = deptObjDef.Id,
            JoinField = "DepartmentId",
            JoinType = "Left",
            SortOrder = 1,
        };

        // Grouping: Department.Name
        var grouping = new ReportGrouping
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            FieldCode = "Name",
            SortOrder = 1,
        };

        report.Fields.Add(deptNameField);
        report.Fields.Add(salaryField);
        report.Relationships.Add(relationship);
        report.Groupings.Add(grouping);

        db.Set<ReportDefinition>().Add(report);
        await db.SaveChangesAsync();

        // Build service dependencies.
        var catalogService = new ObjectCatalogService(db);
        var resolver = new ReportObjectResolver(db, catalogService);
        var execService = new ReportExecutionService(db, userService, resolver);

        // ── Act ───────────────────────────────────────────────────────────────────

        var result = await execService.RunAsync(reportId, page: 1, pageSize: 50, ct: default);

        // ── Assert ────────────────────────────────────────────────────────────────

        result.Should().NotBeNull();
        result.ReportCode.Should().Be(report.Code);

        // The report has groupings, so results should be in Groups (not flat Rows).
        result.Groups.Should().NotBeNull();

        // Each group should have an "BasicSalary" aggregate that is a numeric sum.
        foreach (var group in result.Groups)
        {
            group.Aggregates.Should().ContainKey("BasicSalary",
                because: "BasicSalary is a Sum-aggregated measure field");
        }

        // ── Cleanup (rollback) ────────────────────────────────────────────────────

        await tx.RollbackAsync();
    }

    /// <summary>Minimal <see cref="ICurrentUserService"/> stub for integration tests.</summary>
    private sealed class IntegrationTestUserService : ICurrentUserService
    {
        public IntegrationTestUserService(Guid tenantId) => TenantId = tenantId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; }
        public string? Email => "integration-test@example.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }
}
