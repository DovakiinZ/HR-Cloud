using HR.Application.Common.Exceptions;
using HR.Application.SemanticCatalog;
using HR.Domain.Enums;
using HR.Modules.Platform.Commands.Dashboards;
using HR.Modules.Platform.DTOs.Dashboards;
using HR.Modules.Platform.Services.SemanticCatalog;
using MediatR;
using System.Text.Json;

namespace HR.Modules.Platform.Services.WidgetData;

public sealed class MetricWidgetService : IMetricWidgetService
{
    private readonly ISemanticCatalogProvider _catalog;
    private readonly IWidgetDataService _widgetData;
    private readonly ISender _sender;

    public MetricWidgetService(ISemanticCatalogProvider catalog, IWidgetDataService widgetData, ISender sender)
    {
        _catalog = catalog;
        _widgetData = widgetData;
        _sender = sender;
    }

    public WidgetQuerySpec BuildSpec(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, DateTime nowUtc)
    {
        var metric = _catalog.GetMetric(ctx, metricCode)
            ?? throw new NotFoundException("Metric", metricCode);
        var spec = MetricSpecMapper.ToWidgetSpec(metric.Definition, nowUtc);
        spec.Visualization = string.IsNullOrWhiteSpace(visualization) ? metric.DefaultVisualization : visualization;
        spec.DateGranularity = dateGranularity;
        spec.Limit ??= 12;
        spec.RequiredPermission = metric.RequiredPermissions.FirstOrDefault();
        if (userFilters is { Count: > 0 }) spec.Filters.AddRange(userFilters);
        return spec;
    }

    public async Task<WidgetDataResult> PreviewAsync(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, CancellationToken ct)
    {
        var spec = BuildSpec(ctx, metricCode, userFilters, visualization, dateGranularity, DateTime.UtcNow);
        return await _widgetData.ExecuteAsync(spec, null, ct);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<DashboardWidgetDto> CreateWidgetAsync(Guid dashboardId, CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity,
        string titleAr, string titleEn, WidgetLayoutInput? layout, CancellationToken ct)
    {
        var spec = BuildSpec(ctx, metricCode, userFilters, visualization, dateGranularity, DateTime.UtcNow);
        var viz = spec.Visualization;
        var configuration = JsonSerializer.Serialize(spec, JsonOpts);
        var cmd = new AddDashboardWidgetCommand
        {
            DashboardDefinitionId = dashboardId,
            WidgetType = WidgetTypeFor(viz),
            TitleAr = titleAr,
            TitleEn = string.IsNullOrWhiteSpace(titleEn) ? titleAr : titleEn,
            Configuration = configuration,
            Layout = layout,
        };
        return await _sender.Send(cmd, ct);
    }

    public static WidgetType WidgetTypeFor(string? visualization) => (visualization ?? "") switch
    {
        "Table" or "Leaderboard" => WidgetType.Table,
        "BarChart" or "HorizontalBar" => WidgetType.BarChart,
        "LineChart" or "AreaChart" or "TrendChart" => WidgetType.LineChart,
        "PieChart" => WidgetType.PieChart,
        "DonutChart" => WidgetType.DonutChart,
        _ => WidgetType.KpiCard, // KpiCard, Gauge, unknown
    };
}
