using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance.Export;
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
/// End-to-end export test for <see cref="ReportExportService"/>: seeds a minimal flat report
/// (Employee primary, one visible BasicSalary field), exports it as CSV through the real
/// execution + access + writer stack, and asserts the CSV carries the column header + a data row.
/// Requires REPORTS_TEST_DB; skips cleanly when absent.
/// </summary>
public class ReportExportServiceTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    [SkippableFact]
    public async Task Exports_report_as_csv_bytes()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");

        var tenantId = Guid.NewGuid();
        var userService = new ExportTestUserService(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(options, userService);
        await using var tx = await db.Database.BeginTransactionAsync();

        // Object definition for the primary object (Employee).
        var empObjDef = new ObjectDefinition
        {
            Id = Guid.NewGuid(), Code = "Employee", NameEn = "Employee", NameAr = "موظف",
            Module = "Employees",   // NOT NULL in engine_object_definitions
            TableName = "employees", IsActive = true,
        };
        db.Set<ObjectDefinition>().Add(empObjDef);
        await db.SaveChangesAsync();

        // One employee so the report yields at least one data row.
        var emp = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "E001", FirstName = "Test", LastName = "Export",
            Email = "export@test.example.com", Gender = Gender.Male,
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BasicSalary = 5000m, Status = EmployeeStatus.Active,
        };
        db.Set<Employee>().Add(emp);
        await db.SaveChangesAsync();

        var reportId = Guid.NewGuid();
        var report = new ReportDefinition
        {
            Id = reportId, TenantId = tenantId,
            Code = "TEST_EXPORT_" + reportId.ToString("N")[..8],
            NameEn = "Export Test Report", NameAr = "تقرير تصدير",
            PrimaryObjectId = empObjDef.Id,
            OwnerId = userService.UserId,   // owner → CanRead passes
            IsPublished = true, IsActive = true,
        };
        report.Fields.Add(new ReportField
        {
            Id = Guid.NewGuid(), ReportDefinitionId = reportId,
            FieldType = ReportFieldType.ObjectField, ObjectDefinitionId = empObjDef.Id,
            FieldCode = "BasicSalary", DisplayNameEn = "Basic Salary", DisplayNameAr = "الراتب الأساسي",
            SortOrder = 1, IsVisible = true,
        });
        db.Set<ReportDefinition>().Add(report);
        await db.SaveChangesAsync();

        var catalog = new ObjectCatalogService(db);
        var resolver = new ReportObjectResolver(db, catalog);
        var exec = new ReportExecutionService(db, userService, resolver);
        var access = new ReportAccessService(db, userService);
        var writers = new IExportWriter[] { new CsvExportWriter(), new PdfExportWriter() };
        var svc = new ReportExportService(db, exec, access, writers);

        var file = await svc.ExportAsync(reportId, HR.Application.Engines.Finance.Export.ExportFormat.Csv, parameters: null, default);

        file.ContentType.Should().Be("text/csv");
        file.FileName.Should().EndWith(".csv");
        var text = System.Text.Encoding.UTF8.GetString(file.Content);
        // Headers are Arabic: ReportObjectResolver sets ReportColumn.Label = DisplayNameAr and
        // discards DisplayNameEn, so there is no English label on the run/export path at all.
        text.Should().Contain("الراتب الأساسي");   // column header label
        text.Should().Contain("5000");             // the data row

        await tx.RollbackAsync();
    }

    private sealed class ExportTestUserService : ICurrentUserService
    {
        public ExportTestUserService(Guid tenantId) => TenantId = tenantId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; }
        public string? Email => "export-test@example.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }
}
