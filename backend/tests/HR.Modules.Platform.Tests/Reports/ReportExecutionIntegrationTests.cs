using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.ObjectRegistry;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Core.Entities;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// End-to-end integration test for <see cref="ReportExecutionService"/>.
/// Requires a Postgres connection string in the REPORTS_TEST_DB environment variable.
/// Skips cleanly when the variable is absent so CI unit-test runs are unaffected.
///
/// Seed layout (rolled back after the test):
///   Department "HR"      ← employees with BasicSalary 5000 + 7000  → group Sum = 12000
///   Department "Finance" ← employees with BasicSalary 8000 + 4000  → group Sum = 12000
///
/// The report groups Employee rows by the joined Department.Name column and sums BasicSalary.
/// Field codes used in ReportField, ReportGrouping, and the expected row key must all equal
/// the EF column name exposed by ObjectCatalogService (property name = column name for these
/// entities since no explicit HasColumnName is configured).
/// </summary>
public class ReportExecutionIntegrationTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    // ── Known seed values ─────────────────────────────────────────────────────
    // Chosen to be unambiguous integers with no floating-point precision risk.
    private const decimal HrSalary1      = 5000m;
    private const decimal HrSalary2      = 7000m;
    private const decimal FinanceSalary1 = 8000m;
    private const decimal FinanceSalary2 = 4000m;
    private const double  HrExpectedSum  = 12000d;  // 5000 + 7000
    private const int     ExpectedGroupCount = 2;    // "HR" + "Finance"

    [SkippableFact]
    public async Task Runs_employee_by_department_report_with_sum()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run this integration test.");

        // ── Arrange ──────────────────────────────────────────────────────────

        var tenantId = Guid.NewGuid();
        var userService = new IntegrationTestUserService(tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(Conn)
            .Options;

        await using var db = new ApplicationDbContext(options, userService);

        // Use an ambient transaction that we roll back after the test.
        await using var tx = await db.Database.BeginTransactionAsync();

        // ── 1. Seed ObjectDefinition rows ─────────────────────────────────────
        // The catalog discovers objects via the EF model (CLR type name = object code).
        // ObjectDefinition rows are needed by ReportObjectResolver.BuildModelAsync to look up
        // object Guids from report.PrimaryObjectId / relationship SourceObjectId / TargetObjectId.
        var empObjDef = new ObjectDefinition
        {
            Id = Guid.NewGuid(),
            Code = "Employee",
            NameEn = "Employee",
            NameAr = "موظف",
            Module = "Employees",   // NOT NULL in engine_object_definitions
            TableName = "employees",
            IsActive = true,
        };
        var deptObjDef = new ObjectDefinition
        {
            Id = Guid.NewGuid(),
            Code = "Department",
            NameEn = "Department",
            NameAr = "قسم",
            Module = "Core",        // NOT NULL in engine_object_definitions
            TableName = "departments",
            IsActive = true,
        };
        db.Set<ObjectDefinition>().AddRange(empObjDef, deptObjDef);
        await db.SaveChangesAsync();

        // ── 2. Seed Department entities ───────────────────────────────────────
        // TenantId is set automatically by ApplicationDbContext.SaveChangesAsync when
        // TenantId == Guid.Empty.  The SQL WHERE clause uses _user.TenantId which equals
        // the same tenantId, so only these rows will appear in the query results.
        var hrDept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "HR",
            NameAr = "الموارد البشرية",
            IsActive = true,
        };
        var financeDept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Finance",
            NameAr = "المالية",
            IsActive = true,
        };
        db.Set<Department>().AddRange(hrDept, financeDept);
        await db.SaveChangesAsync();

        // ── 3. Seed Employee entities ─────────────────────────────────────────
        // DepartmentId FK must point at the seeded Department rows so the LEFT JOIN produces
        // non-null Name values that the shaper can group on.
        //   HR dept:      5000 + 7000 = 12000
        //   Finance dept: 8000 + 4000 = 12000
        var employees = new[]
        {
            MakeEmployee("E001", HrSalary1,      hrDept.Id),
            MakeEmployee("E002", HrSalary2,      hrDept.Id),
            MakeEmployee("E003", FinanceSalary1, financeDept.Id),
            MakeEmployee("E004", FinanceSalary2, financeDept.Id),
        };
        db.Set<Employee>().AddRange(employees);
        await db.SaveChangesAsync();

        // ── 4. Seed ReportDefinition ──────────────────────────────────────────
        // Primary object: Employee.
        // Joined object:  Department (LEFT JOIN via Employee.DepartmentId → Department.Id).
        // Visible fields:
        //   - "Name"        on Department (FieldCode matches ObjectCatalogService field key = EF column name)
        //   - "BasicSalary" on Employee   (FieldCode matches ObjectCatalogService field key = EF column name)
        // Grouping FieldCode = "Name" — must equal ReportField.FieldCode of the dimension column and the
        // OutputCode written into ReportRow, which the shaper uses as the grouping key.
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

        // Dimension: Department.Name — FieldCode = "Name" (column name on "departments" table).
        var deptNameField = new ReportField
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            FieldType = ReportFieldType.ObjectField,
            ObjectDefinitionId = deptObjDef.Id,
            FieldCode = "Name",           // ← must match the column exposed by ObjectCatalogService
            DisplayNameEn = "Department Name",
            DisplayNameAr = "اسم القسم",
            SortOrder = 1,
            IsVisible = true,
        };

        // Measure: Employee.BasicSalary — FieldCode = "BasicSalary" (column name on "employees" table).
        var salaryField = new ReportField
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            FieldType = ReportFieldType.ObjectField,
            ObjectDefinitionId = empObjDef.Id,
            FieldCode = "BasicSalary",    // ← must match the column exposed by ObjectCatalogService
            DisplayNameEn = "Basic Salary",
            DisplayNameAr = "الراتب الأساسي",
            Aggregation = AggregationType.Sum,
            SortOrder = 2,
            IsVisible = true,
        };

        // Relationship: Employee (source) → Department (target) via DepartmentId FK.
        // JoinField = "DepartmentId" — must be a real field on the source (Employee) catalog object.
        // ReportSqlBuilder emits: LEFT JOIN "departments" t1 ON t1."Id" = t0."DepartmentId"
        var relationship = new ReportRelationship
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            SourceObjectId = empObjDef.Id,
            TargetObjectId = deptObjDef.Id,
            JoinField = "DepartmentId",   // ← Employee has this property/column
            JoinType = "Left",
            SortOrder = 1,
        };

        // Grouping: FieldCode = "Name" must equal the FieldCode of the dimension ReportField above.
        // ReportRowShaper groups rows by r.GetValueOrDefault(groupCode) where groupCode = this FieldCode.
        // ReportExecutionService stores row values keyed by OutputCode = ReportField.FieldCode = "Name".
        // So "Name" in the grouping matches the "Name" key in ReportRow → groups are non-empty.
        var grouping = new ReportGrouping
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = reportId,
            FieldCode = "Name",           // ← must equal deptNameField.FieldCode
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

        // ── Act ───────────────────────────────────────────────────────────────

        var result = await execService.RunAsync(reportId, page: 1, pageSize: 50, parameters: null, ct: default);

        // ── Assert ────────────────────────────────────────────────────────────

        result.Should().NotBeNull();
        result.ReportCode.Should().Be(report.Code);

        // The report has groupings, so rows must be in Groups (not flat Rows).
        // Assert non-empty FIRST to prevent vacuous passes when the query returns 0 rows.
        result.Groups.Should().NotBeNullOrEmpty(
            because: "the report has a Grouping, seeded employees exist, and their tenant matches the query filter");

        // Exact group count: one group per department (HR + Finance = 2).
        result.Groups.Should().HaveCount(ExpectedGroupCount,
            because: "two departments were seeded and each has at least one employee");

        // Each group must carry a numeric sum for BasicSalary.
        foreach (var group in result.Groups)
        {
            group.Aggregates.Should().ContainKey("BasicSalary",
                because: "BasicSalary is a Sum-aggregated measure field present on every group");
            group.Aggregates["BasicSalary"].Should().BeGreaterThan(0,
                because: "all seeded salaries are positive");
        }

        // Concrete value check for the known "HR" department group.
        // The group's Key is the department Name string as read from the DB.
        var hrGroup = result.Groups.FirstOrDefault(g => string.Equals(g.Key?.ToString(), "HR", StringComparison.Ordinal));
        hrGroup.Should().NotBeNull(because: "a department named 'HR' was seeded");
        hrGroup!.Aggregates["BasicSalary"].Should().BeApproximately(HrExpectedSum, precision: 0.01d,
            because: $"HR department has two employees with salaries {HrSalary1} + {HrSalary2} = {HrExpectedSum}");

        // ── Cleanup (rollback) ────────────────────────────────────────────────

        await tx.RollbackAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Employee MakeEmployee(string number, decimal salary, Guid departmentId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = number,
        FirstName = "Test",
        LastName = number,
        Email = $"{number}@test.example.com",
        Gender = Gender.Male,
        DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        BasicSalary = salary,
        DepartmentId = departmentId,
        Status = EmployeeStatus.Active,
    };

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
