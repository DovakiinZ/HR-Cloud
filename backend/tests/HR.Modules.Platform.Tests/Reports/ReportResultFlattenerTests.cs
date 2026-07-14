using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportResultFlattenerTests
{
    private static ReportColumn Dim(string code) => new() { Code = code, Label = code, IsMeasure = false };
    private static ReportColumn Measure(string code) => new() { Code = code, Label = code, IsMeasure = true };

    [Fact]
    public void Flat_report_projects_rows_and_columns()
    {
        var result = new ReportResult
        {
            Columns = new() { Dim("Name"), Measure("Salary") },
            Rows = new()
            {
                new ReportRow(new Dictionary<string, object?> { ["Name"] = "A", ["Salary"] = 100.0 }),
                new ReportRow(new Dictionary<string, object?> { ["Name"] = "B", ["Salary"] = 200.0 }),
            },
        };
        var ds = ReportResultFlattener.Flatten(result, "T");
        ds.Columns.Select(c => c.Key).Should().Equal("Name", "Salary");
        ds.Columns.Single(c => c.Key == "Salary").Align.Should().Be(TabularAlign.End);
        ds.Rows.Should().HaveCount(2);
        ds.Rows[0]["Name"].Should().Be("A");
        ds.Rows[1]["Salary"].Should().Be(200.0);
    }

    [Fact]
    public void Grouped_report_emits_data_rows_then_subtotal_then_grand_total()
    {
        var result = new ReportResult
        {
            Columns = new() { Dim("Dept"), Measure("Salary") },
            Groups = new()
            {
                new ReportGroup
                {
                    FieldCode = "Dept", Key = "HR", Label = "HR",
                    Rows = new() { new ReportRow(new Dictionary<string, object?> { ["Dept"] = "HR", ["Salary"] = 100.0 }) },
                    Aggregates = new() { ["Salary"] = 100.0 },
                },
            },
            GrandTotals = new() { ["Salary"] = 100.0 },
        };
        var ds = ReportResultFlattener.Flatten(result, "T");
        // 1 data row + 1 subtotal + 1 grand total
        ds.Rows.Should().HaveCount(3);
        ds.Rows[1]["Dept"].Should().Be("HR — subtotal");
        ds.Rows[1]["Salary"].Should().Be(100.0);
        ds.Rows[2]["Dept"].Should().Be("Grand Total");
        ds.Rows[2]["Salary"].Should().Be(100.0);
    }
}
