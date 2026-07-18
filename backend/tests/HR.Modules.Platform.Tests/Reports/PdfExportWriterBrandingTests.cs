using System.Collections.Generic;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class PdfExportWriterBrandingTests
{
    private static TabularDataset Data() => new(
        "موظفون حسب الإدارات",
        new List<TabularColumn> { new("name", "اسم الموظف"), new("dept", "الإدارة") },
        new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { ["name"] = "رُبا", ["dept"] = "الموارد البشرية" } });

    [Fact]
    public void Write_with_arabic_and_branding_produces_pdf()
    {
        var branding = new CompanyBranding("شركة الاختبار", "Test Co", null, "1010101010", "300000000000003", "0500000000", "hr@test.sa", "الرياض");
        var bytes = new PdfExportWriter().Write(Data(), new ExportWriteOptions(Branding: branding));
        bytes.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-"); // valid PDF magic
    }

    [Fact]
    public void Write_without_branding_still_works()
        => new PdfExportWriter().Write(Data(), null).Should().NotBeNullOrEmpty();
}
