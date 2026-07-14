using System.Collections.Generic;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class PdfExportWriterTests
{
    [Fact]
    public void Writes_a_nonempty_pdf_document()
    {
        var ds = new TabularDataset("Employees", new List<TabularColumn>
            { new("Name", "Name"), new("Salary", "Salary", TabularAlign.End) },
            new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["Name"] = "Alice", ["Salary"] = 5000m },
                new Dictionary<string, object?> { ["Name"] = "Bob",   ["Salary"] = 7000m },
            });
        var writer = new PdfExportWriter();
        writer.Format.Should().Be(ExportFormat.Pdf);
        writer.ContentType.Should().Be("application/pdf");
        var bytes = writer.Write(ds);
        bytes.Should().NotBeNullOrEmpty();
        // PDF magic header "%PDF"
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }
}
