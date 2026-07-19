using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.Dashboards;

public class WidgetResultFlattenerTests
{
    [Fact]
    public void Scalar_flattens_to_one_cell()
    {
        var ds = WidgetResultFlattener.Flatten(new WidgetDataResult { Kind = "scalar", Value = 42 }, "KPI");
        ds.Columns.Select(c => c.Key).Should().Equal("value");
        ds.Rows.Should().HaveCount(1);
        ds.Rows[0]["value"].Should().Be(42.0);
    }

    [Fact]
    public void Series_flattens_to_label_value_rows()
    {
        var result = new WidgetDataResult { Kind = "series", Series = new()
            { new SeriesPoint { Key = "hr", Label = "HR", Value = 3 }, new SeriesPoint { Key = "it", Label = "IT", Value = 5 } } };
        var ds = WidgetResultFlattener.Flatten(result, "By Dept");
        ds.Columns.Select(c => c.Key).Should().Equal("label", "value");
        ds.Rows.Should().HaveCount(2);
        ds.Rows[1]["label"].Should().Be("IT");
        ds.Rows[1]["value"].Should().Be(5.0);
    }

    [Fact]
    public void Table_flattens_columns_and_rows()
    {
        var result = new WidgetDataResult
        {
            Kind = "table",
            Columns = new() { new TableColumn { Code = "Name", Label = "Name", Type = "Text" }, new TableColumn { Code = "Salary", Label = "Salary", Type = "Currency" } },
            Rows = new() { new Dictionary<string, object?> { ["Name"] = "Ali", ["Salary"] = 5000 } },
        };
        var ds = WidgetResultFlattener.Flatten(result, "T");
        ds.Columns.Select(c => c.Key).Should().Equal("Name", "Salary");
        ds.Columns.Single(c => c.Key == "Salary").Align.Should().Be(TabularAlign.End);
        ds.Rows[0]["Name"].Should().Be("Ali");
        ds.Rows[0]["Salary"].Should().Be(5000);
    }
}
