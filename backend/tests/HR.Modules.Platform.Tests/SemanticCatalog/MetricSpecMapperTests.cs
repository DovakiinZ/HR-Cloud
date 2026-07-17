using System;
using System.Collections.Generic;
using FluentAssertions;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class MetricSpecMapperTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
    private static IReadOnlyList<SemanticMetricFilter> NoFilters => Array.Empty<SemanticMetricFilter>();

    [Fact]
    public void Simple_count_passes_through()
    {
        var def = new SemanticMetricDefinition("Employee", "Count", null, NoFilters, null);
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.ObjectCode.Should().Be("Employee");
        spec.Aggregation.Should().Be("Count");
        spec.GroupByField.Should().BeNull();
        spec.Filters.Should().BeEmpty();
    }

    [Fact]
    public void Enum_equals_filter_translates_operator()
    {
        var def = new SemanticMetricDefinition("AttendanceRecord", "Count", null,
            new[] { new SemanticMetricFilter("Status", "Equals", Value: "6") }, null);
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.Filters.Should().ContainSingle();
        spec.Filters[0].Field.Should().Be("Status");
        spec.Filters[0].Operator.Should().Be("eq");
        spec.Filters[0].Value.Should().Be("6");
    }

    [Fact]
    public void Relative_date_filter_resolves_to_literal()
    {
        var def = new SemanticMetricDefinition("Employee", "Count", null,
            new[] { new SemanticMetricFilter("HireDate", "GreaterThanOrEqual", RelativeValue: "startOfMonth") }, null);
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.Filters[0].Operator.Should().Be("gte");
        spec.Filters[0].Value.Should().Be("2026-07-01");
    }

    [Fact]
    public void Formula_metric_maps_measures()
    {
        var def = new SemanticMetricDefinition("LeaveBalance", "Formula", null, NoFilters, null,
            Formula: "m1 + m2 - m3",
            Measures: new[]
            {
                new SemanticMetricMeasure("m1", "Sum", "EntitledDays", NoFilters),
                new SemanticMetricMeasure("m2", "Sum", "CarriedForwardDays", NoFilters),
                new SemanticMetricMeasure("m3", "Sum", "UsedDays", NoFilters),
            });
        var spec = MetricSpecMapper.ToWidgetSpec(def, Now);
        spec.Aggregation.Should().Be("Formula");
        spec.Formula.Should().Be("m1 + m2 - m3");
        spec.Measures.Should().HaveCount(3);
        spec.Measures![0].Name.Should().Be("m1");
        spec.Measures[0].AggregationField.Should().Be("EntitledDays");
    }
}
