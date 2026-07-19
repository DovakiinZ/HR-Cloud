using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Application.SemanticCatalog;
using HR.Application.SemanticCatalog.Contracts;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.WidgetData;

public class MetricWidgetServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeCatalog : ISemanticCatalogProvider
    {
        private readonly SemanticMetric? _m;
        public FakeCatalog(SemanticMetric? m) => _m = m;
        public SemanticMetric? GetMetric(CatalogQueryContext ctx, string code) => code == _m?.Code ? _m : null;
        public IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext c) => Array.Empty<SemanticDomain>();
        public IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext c, string? d = null) => Array.Empty<SemanticObject>();
        public SemanticObject? GetObject(CatalogQueryContext c, string code) => null;
        public IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext c, string? d = null) => Array.Empty<SemanticMetric>();
        public IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext c, string q) => Array.Empty<SemanticSearchHit>();
        public CatalogHealth GetHealth() => new(0,0,0,0, Array.Empty<HiddenItem>());
    }

    private static SemanticMetric Metric(string agg = "Count", string? field = null, string viz = "KpiCard",
        params SemanticMetricFilter[] baked)
        => new("m1","اسم","Name","وصف","Desc","Icon","employees", new[]{"Employees.View"},
            new SemanticMetricDefinition("Employee", agg, field, baked, null), viz, new[]{"DepartmentId"});

    private static MetricWidgetService Sut(SemanticMetric? m)
        => new(new FakeCatalog(m), widgetData: null!, sender: null!);

    private static CatalogQueryContext Ctx => new(new[] { "Employees.View" });

    [Fact]
    public void BuildSpec_maps_metric_and_sets_visualization_default()
    {
        var spec = Sut(Metric(viz: "BarChart")).BuildSpec(Ctx, "m1", Array.Empty<WidgetFilterSpec>(), null, null, Now);
        spec.ObjectCode.Should().Be("Employee");
        spec.Aggregation.Should().Be("Count");
        spec.Visualization.Should().Be("BarChart");   // from metric.DefaultVisualization
        spec.Limit.Should().Be(12);
    }

    [Fact]
    public void BuildSpec_visualization_override_wins()
    {
        var spec = Sut(Metric(viz: "BarChart")).BuildSpec(Ctx, "m1", Array.Empty<WidgetFilterSpec>(), "Table", "month", Now);
        spec.Visualization.Should().Be("Table");
        spec.DateGranularity.Should().Be("month");
    }

    [Fact]
    public void BuildSpec_appends_user_filters_after_baked()
    {
        var baked = new SemanticMetricFilter("Status", "Equals", Value: "1");
        var user = new WidgetFilterSpec { Field = "DepartmentId", Operator = "eq", Value = "abc" };
        var spec = Sut(Metric(baked: baked)).BuildSpec(Ctx, "m1", new[] { user }, null, null, Now);
        spec.Filters.Should().HaveCount(2);
        spec.Filters[0].Field.Should().Be("Status");        // baked first
        spec.Filters[1].Field.Should().Be("DepartmentId");  // user after
    }

    [Fact]
    public void BuildSpec_throws_NotFound_when_metric_missing_or_denied()
    {
        FluentActions.Invoking(() => Sut(Metric()).BuildSpec(Ctx, "nope", Array.Empty<WidgetFilterSpec>(), null, null, Now))
            .Should().Throw<HR.Application.Common.Exceptions.NotFoundException>();
    }

    [Theory]
    [InlineData("KpiCard", WidgetType.KpiCard)]
    [InlineData("Gauge", WidgetType.KpiCard)]
    [InlineData("BarChart", WidgetType.BarChart)]
    [InlineData("HorizontalBar", WidgetType.BarChart)]
    [InlineData("LineChart", WidgetType.LineChart)]
    [InlineData("PieChart", WidgetType.PieChart)]
    [InlineData("DonutChart", WidgetType.DonutChart)]
    [InlineData("Table", WidgetType.Table)]
    [InlineData("Leaderboard", WidgetType.Table)]
    [InlineData("something-unknown", WidgetType.KpiCard)]
    public void WidgetTypeFor_maps(string viz, WidgetType expected)
        => MetricWidgetService.WidgetTypeFor(viz).Should().Be(expected);
}
