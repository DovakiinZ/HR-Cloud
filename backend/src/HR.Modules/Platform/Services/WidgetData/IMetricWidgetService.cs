using HR.Application.SemanticCatalog;
using HR.Modules.Platform.Commands.Dashboards;
using HR.Modules.Platform.DTOs.Dashboards;

namespace HR.Modules.Platform.Services.WidgetData;

public interface IMetricWidgetService
{
    WidgetQuerySpec BuildSpec(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, DateTime nowUtc);
    Task<WidgetDataResult> PreviewAsync(CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity, CancellationToken ct);
    Task<DashboardWidgetDto> CreateWidgetAsync(Guid dashboardId, CatalogQueryContext ctx, string metricCode,
        IReadOnlyList<WidgetFilterSpec> userFilters, string? visualization, string? dateGranularity,
        string titleAr, string titleEn, WidgetLayoutInput? layout, CancellationToken ct);
}
