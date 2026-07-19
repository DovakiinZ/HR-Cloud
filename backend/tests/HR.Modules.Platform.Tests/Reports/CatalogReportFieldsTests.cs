using System.Linq;
using FluentAssertions;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class CatalogReportFieldsTests
{
    private static System.Collections.Generic.IEnumerable<HR.Application.SemanticCatalog.Contracts.SemanticField> AllFields()
        => CatalogRegistry.Objects.SelectMany(o => o.Fields);

    [Fact]
    public void Some_fields_are_report_enabled()
        => AllFields().Any(f => f.ReportEnabled).Should().BeTrue();

    [Fact]
    public void Manager_reference_label_is_correct_arabic()
    {
        var mgr = AllFields().FirstOrDefault(f => f.ObjectCode == "Employee" && f.FieldCode == "ManagerId");
        mgr.Should().NotBeNull();
        mgr!.NameAr.Should().Be("المدير المباشر");        // exact — proves no corruption/reversal
        mgr.ReportEnabled.Should().BeTrue();
    }

    [Fact]
    public void Department_reference_label_is_correct_arabic()
        => AllFields().First(f => f.ObjectCode == "Employee" && f.FieldCode == "DepartmentId").NameAr.Should().Be("القسم");
}
