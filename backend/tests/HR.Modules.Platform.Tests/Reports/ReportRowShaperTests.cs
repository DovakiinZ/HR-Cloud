using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportRowShaperTests
{
    private readonly ReportRowShaper _shaper = new(new ComputedFieldEvaluator());

    private static ReportRow Row(string dept, decimal salary) =>
        new() { ["Department"] = dept, ["Salary"] = salary };

    private static ReportShapeSpec Spec(bool grouped) => new()
    {
        ReportCode = "TEST",
        Columns = new()
        {
            new ReportColumn { Code = "Department", Label = "Dept", Type = "Text" },
            new ReportColumn { Code = "Salary", Label = "Salary", Type = "Number", IsMeasure = true, Aggregation = AggregationType.Sum },
        },
        GroupByCodes = grouped ? new() { "Department" } : new(),
        Page = 1, PageSize = 50,
    };

    [Fact]
    public void Groups_rows_and_sums_measures()
    {
        var rows = new List<ReportRow> { Row("HR", 100m), Row("HR", 200m), Row("IT", 50m) };
        var result = _shaper.Shape(rows, Spec(grouped: true));

        result.Groups.Should().HaveCount(2);
        var hr = result.Groups.Single(g => (string)g.Key! == "HR");
        hr.Count.Should().Be(2);
        hr.Aggregates["Salary"].Should().Be(300);
        result.GrandTotals["Salary"].Should().Be(350);
    }

    [Fact]
    public void Flat_result_pages_rows_when_no_grouping()
    {
        var rows = Enumerable.Range(0, 120).Select(i => Row("HR", i)).ToList();
        var spec = Spec(grouped: false); spec.PageSize = 50;
        var result = _shaper.Shape(rows, spec);
        result.Groups.Should().BeEmpty();
        result.Rows.Should().HaveCount(50);
        result.TotalCount.Should().Be(120);
    }
}
