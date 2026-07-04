using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP5 Task 1/2 — format-agnostic dataset + pure format writers (CSV RFC-4180, TXT delimited/
/// fixed-width). The writers are the "Exporter" layer; a new format is a new writer, engine unchanged.</summary>
public class ExportWritersTests
{
    private static TabularDataset Sample() => new(
        "Payroll",
        new[]
        {
            new TabularColumn("num", "الرقم"),
            new TabularColumn("name", "الاسم"),
            new TabularColumn("net", "الصافي"),
        },
        new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["num"] = "E1", ["name"] = "Ali, A", ["net"] = 5750m },
            new Dictionary<string, object?> { ["num"] = "E2", ["name"] = "Say \"hi\"", ["net"] = 1000m },
        });

    [Fact]
    public void Csv_writes_header_and_rows_with_rfc4180_escaping()
    {
        var bytes = new CsvExportWriter().Write(Sample());
        var text = Encoding.UTF8.GetString(bytes);

        text.Should().Contain("الرقم,الاسم,الصافي");
        text.Should().Contain("E1,\"Ali, A\",5750");           // comma in value → quoted
        text.Should().Contain("E2,\"Say \"\"hi\"\"\",1000");   // quotes doubled
    }

    [Fact]
    public void Csv_writer_reports_format_content_type_and_extension()
    {
        var w = new CsvExportWriter();
        w.Format.Should().Be(ExportFormat.Csv);
        w.ContentType.Should().Be("text/csv");
        w.Extension.Should().Be("csv");
    }

    [Fact]
    public void Txt_fixed_width_pads_each_column_to_its_width()
    {
        var ds = new TabularDataset("t",
            new[] { new TabularColumn("a", "A", TabularAlign.Start, 5), new TabularColumn("b", "B", TabularAlign.Start, 3) },
            new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["a"] = "XY", ["b"] = "Z" },
            });
        var text = Encoding.UTF8.GetString(new TxtExportWriter().Write(ds, new ExportWriteOptions(FixedWidth: true, IncludeHeader: false)));
        // "XY" padded to 5 + "Z" padded to 3 = "XY   Z  "
        text.TrimEnd('\r', '\n').Should().Be("XY   Z  ");
    }
}
