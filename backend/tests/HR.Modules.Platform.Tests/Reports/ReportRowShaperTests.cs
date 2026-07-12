using FluentAssertions;
using HR.Domain.Engines.Finance.Expressions;
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

    /// <summary>
    /// Two-key grouping: Department (level 0) → Grade (level 1), measure = Salary Sum.
    /// Seed data:
    ///   Engineering / Senior  → 5000 + 6000 = 11 000
    ///   Engineering / Junior  → 3000
    ///   Engineering total     → 14 000
    ///   Finance / Senior      → 4000
    ///   Finance total         → 4 000
    ///   Grand total           → 18 000
    /// </summary>
    [Fact]
    public void Two_key_grouping_builds_correct_subgroups_and_aggregates()
    {
        var rows = new List<ReportRow>
        {
            new() { ["Department"] = "Engineering", ["Grade"] = "Senior", ["Salary"] = 5000m },
            new() { ["Department"] = "Engineering", ["Grade"] = "Senior", ["Salary"] = 6000m },
            new() { ["Department"] = "Engineering", ["Grade"] = "Junior", ["Salary"] = 3000m },
            new() { ["Department"] = "Finance",      ["Grade"] = "Senior", ["Salary"] = 4000m },
        };

        var spec = new ReportShapeSpec
        {
            ReportCode = "TWO_KEY",
            Columns = new()
            {
                new ReportColumn { Code = "Department", Label = "Department", Type = "Text" },
                new ReportColumn { Code = "Grade",      Label = "Grade",      Type = "Text" },
                new ReportColumn { Code = "Salary",     Label = "Salary",     Type = "Number",
                                   IsMeasure = true, Aggregation = AggregationType.Sum },
            },
            GroupByCodes = new() { "Department", "Grade" },
            Page = 1, PageSize = 50,
        };

        var result = _shaper.Shape(rows, spec);

        // Top-level: 2 departments
        result.Groups.Should().HaveCount(2);

        // Top-level groups must have SubGroups, not leaf Rows
        result.Groups.Should().AllSatisfy(g => g.Rows.Should().BeEmpty());

        // Engineering group
        var eng = result.Groups.Single(g => g.Key?.ToString() == "Engineering");
        eng.Aggregates["Salary"].Should().Be(14_000);

        // Engineering subgroups: Senior + Junior
        eng.SubGroups.Should().HaveCount(2);
        var engSenior = eng.SubGroups.Single(g => g.Key?.ToString() == "Senior");
        engSenior.Aggregates["Salary"].Should().Be(11_000);
        engSenior.Rows.Should().HaveCount(2);   // leaf level: rows here
        engSenior.SubGroups.Should().BeEmpty();

        var engJunior = eng.SubGroups.Single(g => g.Key?.ToString() == "Junior");
        engJunior.Aggregates["Salary"].Should().Be(3_000);
        engJunior.Rows.Should().HaveCount(1);

        // Finance group
        var fin = result.Groups.Single(g => g.Key?.ToString() == "Finance");
        fin.Aggregates["Salary"].Should().Be(4_000);
        fin.SubGroups.Should().HaveCount(1);
        fin.SubGroups[0].Rows.Should().HaveCount(1);

        // Grand total
        result.GrandTotals["Salary"].Should().Be(18_000);
    }

    /// <summary>
    /// A computed column whose AST references a variable that is NOT present in the row
    /// must NOT throw — the cell should be silently set to null so the rest of the report renders.
    /// </summary>
    [Fact]
    public void Computed_column_with_unknown_variable_sets_cell_to_null_and_does_not_throw()
    {
        // Build an AST that references a variable "NonExistentField" — not in the row.
        var badAst = new VariableExpr("NonExistentField");

        var spec = new ReportShapeSpec
        {
            ReportCode = "COMPUTED_FAULT",
            Columns = new() { new ReportColumn { Code = "Salary", Label = "Salary", Type = "Number" } },
            Computed = new() { new ComputedColumnSpec { Code = "Bonus", Ast = badAst } },
            GroupByCodes = new(),
            Page = 1, PageSize = 50,
        };

        var rows = new List<ReportRow> { new() { ["Salary"] = 5000m } };

        ReportResult result = default!;
        var act = () => { result = _shaper.Shape(rows, spec); };

        act.Should().NotThrow();
        result.Rows.Should().HaveCount(1);
        result.Rows[0].GetValueOrDefault("Bonus").Should().BeNull("a formula error must yield null, not throw");
    }

    /// <summary>
    /// Multi-key InMemorySorts: primary = Dept ASC, secondary = Salary DESC.
    /// Seed rows whose correct order differs from "primary only":
    ///   ("Alpha", 100), ("Alpha", 300), ("Alpha", 200), ("Beta", 50), ("Beta", 150)
    /// Expected after primary ASC + secondary DESC:
    ///   ("Alpha",300), ("Alpha",200), ("Alpha",100), ("Beta",150), ("Beta",50)
    /// The "primary only" ordering would NOT guarantee salary order within each dept.
    /// </summary>
    [Fact]
    public void Multi_key_sort_applies_primary_then_secondary_ordering()
    {
        var rows = new List<ReportRow>
        {
            new() { ["Dept"] = "Alpha", ["Salary"] = 100m },
            new() { ["Dept"] = "Alpha", ["Salary"] = 300m },
            new() { ["Dept"] = "Alpha", ["Salary"] = 200m },
            new() { ["Dept"] = "Beta",  ["Salary"] = 50m  },
            new() { ["Dept"] = "Beta",  ["Salary"] = 150m },
        };

        var spec = new ReportShapeSpec
        {
            ReportCode = "SORT_TEST",
            Columns = new()
            {
                new ReportColumn { Code = "Dept",   Label = "Dept",   Type = "Text" },
                new ReportColumn { Code = "Salary", Label = "Salary", Type = "Number",
                                   IsMeasure = true, Aggregation = AggregationType.Sum },
            },
            GroupByCodes = new(),   // flat, no grouping
            InMemorySorts = new()
            {
                ("Dept",   SortDirection.Ascending),
                ("Salary", SortDirection.Descending),
            },
            Page = 1, PageSize = 50,
        };

        var result = _shaper.Shape(rows, spec);

        result.Rows.Should().HaveCount(5);

        // Primary: Dept ASC — all Alphas before all Betas
        var depts = result.Rows.Select(r => r["Dept"]?.ToString()).ToList();
        depts.Should().Equal("Alpha", "Alpha", "Alpha", "Beta", "Beta");

        // Secondary: Salary DESC within each dept
        var alphas = result.Rows.Where(r => r["Dept"]?.ToString() == "Alpha")
                                .Select(r => Convert.ToDecimal(r["Salary"])).ToList();
        alphas.Should().Equal(300m, 200m, 100m);

        var betas = result.Rows.Where(r => r["Dept"]?.ToString() == "Beta")
                               .Select(r => Convert.ToDecimal(r["Salary"])).ToList();
        betas.Should().Equal(150m, 50m);
    }
}
