using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.SemanticCatalog;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.SemanticCatalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class CodeDefinedSemanticCatalogTests
{
    // Fake catalog: only the objects/fields we declare exist.
    private sealed class FakeCatalog : IObjectCatalogService
    {
        private readonly Dictionary<string, CatalogObjectDto> _objs;
        public FakeCatalog(params CatalogObjectDto[] objs) => _objs = objs.ToDictionary(o => o.Code);
        public IReadOnlyList<CatalogObjectDto> GetCatalog() => _objs.Values.ToList();
        public CatalogObjectDto? GetObject(string code) => _objs.GetValueOrDefault(code);
        public ResolvedObject? Resolve(string code) => null; // not used by the provider
    }

    private static CatalogFieldDto F(string code) => new()
    {
        Code = code, NameEn = code, NameAr = code, FieldType = "Text",
        IsFilterable = true,
    };

    private static CatalogObjectDto Obj(string code, params string[] fields) => new()
    {
        Code = code, NameEn = code, NameAr = code, Module = "X",
        Fields = fields.Select(F).ToList(),
    };

    // A catalog rich enough for the resolvable metrics under test.
    private static IObjectCatalogService FullCatalog() => new FakeCatalog(
        Obj("Employee", "Status", "HireDate", "DepartmentId", "BranchId", "JobTitleId", "ContractEndDate", "BasicSalary", "FirstNameAr"),
        Obj("PayrollPayslip", "GrossEarnings", "NetAmount", "TotalDeductions"),
        Obj("AttendanceRecord", "Status", "OvertimeMinutes"),
        Obj("LeaveBalance", "EntitledDays", "CarriedForwardDays", "UsedDays"),
        Obj("RequestInstance", "Status"),
        Obj("EmployeeDocument", "ExpiryDate"),
        Obj("Loan"), Obj("Expense"));

    private static CodeDefinedSemanticCatalog Sut(IObjectCatalogService cat)
        => new(cat, NullLogger<CodeDefinedSemanticCatalog>.Instance);

    private static CatalogQueryContext All => new(new[]
        { "Employees.View","Payroll.View","Attendance.View","Leaves.View","Requests.View","Platform.Dashboards.View" });

    [Fact]
    public void Resolvable_metrics_are_visible_self_hiders_are_not()
    {
        var sut = Sut(FullCatalog());
        var codes = sut.GetMetrics(All).Select(m => m.Code).ToList();
        codes.Should().Contain(new[] { "total_employees","net_payroll","remaining_leave_balance","expiring_documents" });
        codes.Should().NotContain(new[] { "total_gosi","total_additions","pending_approvals" });
    }

    [Fact]
    public void Health_reports_self_hidden_metrics_with_reasons()
    {
        var health = Sut(FullCatalog()).GetHealth();
        var hidden = health.Hidden.Where(h => h.Kind == "Metric").Select(h => h.Code).ToList();
        hidden.Should().Contain(new[] { "total_gosi","total_additions","pending_approvals" });
        health.Hidden.Single(h => h.Code == "total_gosi").Reason.Should().Contain("GosiAmount");
    }

    [Fact]
    public void Permission_filter_hides_payroll_metrics_without_permission()
    {
        var sut = Sut(FullCatalog());
        var ctx = new CatalogQueryContext(new[] { "Employees.View" });
        var codes = sut.GetMetrics(ctx).Select(m => m.Code).ToList();
        codes.Should().Contain("total_employees");
        codes.Should().NotContain("net_payroll"); // needs Payroll.View
    }

    [Fact]
    public void Object_missing_from_catalog_is_hidden()
    {
        // Catalog WITHOUT PayrollPayslip → payroll object + its metrics hide.
        var cat = new FakeCatalog(Obj("Employee", "Status", "HireDate", "DepartmentId", "ContractEndDate"));
        var sut = Sut(cat);
        sut.GetObjects(All).Select(o => o.ObjectCode).Should().NotContain("PayrollPayslip");
        sut.GetMetrics(All).Select(m => m.Code).Should().NotContain("net_payroll");
    }

    [Fact]
    public void Search_matches_arabic_and_synonyms()
    {
        var sut = Sut(FullCatalog());
        sut.Search(All, "راتب").Select(h => h.Code).Should().Contain("net_payroll");
        sut.Search(All, "late").Select(h => h.Code).Should().Contain("late_employees");
        sut.Search(All, "تأخير").Select(h => h.Code).Should().Contain("late_employees");
    }

    [Fact]
    public void DefaultFilters_and_DefaultSort_referencing_missing_fields_are_dropped()
    {
        // Employee in the registry has DefaultFilters for DepartmentId + BranchId,
        // and DefaultSort on HireDate.  Build a fake catalog where BranchId and
        // HireDate are absent — only DepartmentId (and Status) exist.
        var cat = new FakeCatalog(
            Obj("Employee", "Status", "DepartmentId"));   // HireDate + BranchId intentionally absent

        var sut = Sut(cat);
        var obj = sut.GetObject(All, "Employee");

        obj.Should().NotBeNull("Employee object itself must still be visible");

        // BranchId filter should be dropped; DepartmentId filter must survive
        obj!.DefaultFilters.Select(f => f.FieldCode)
            .Should().NotContain("BranchId")
            .And.Contain("DepartmentId");

        // HireDate is missing → DefaultSort must be nulled
        obj.DefaultSort.Should().BeNull("HireDate is absent from the live catalog object");
    }
}
