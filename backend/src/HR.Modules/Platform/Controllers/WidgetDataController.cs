using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Models;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.WidgetData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

/// <summary>
/// Executes widget query specs against live data (the engine behind every KPI/chart).
/// Object-driven and injection-safe; tenant + soft-delete scoping is automatic.
/// </summary>
[Authorize]
[Route("api/platform/dashboards/widget-data")]
public class WidgetDataController : BaseApiController
{
    private readonly IWidgetDataService _data;
    private readonly IWidgetSuggestionService _suggest;
    private readonly IWidgetExportService _export;
    public WidgetDataController(IWidgetDataService data, IWidgetSuggestionService suggest, IWidgetExportService export)
    {
        _data = data; _suggest = suggest; _export = export;
    }

    /// <summary>AI builder — turn a natural-language phrase into a ready widget spec.</summary>
    [HttpPost("ai-suggest")]
    [RequirePermission("Platform.Dashboards.View")]
    public ActionResult<ApiResponse<WidgetSuggestion>> AiSuggest([FromBody] AiSuggestRequest req)
        => OkResponse(_suggest.Suggest(req.Prompt ?? ""));

    /// <summary>Live preview from the builder — execute an ad-hoc spec without saving.</summary>
    [HttpPost("preview")]
    [RequirePermission("Platform.Dashboards.View")]
    public async Task<ActionResult<ApiResponse<WidgetDataResult>>> Preview([FromBody] PreviewRequest req, CancellationToken ct)
        => OkResponse(await _data.ExecuteAsync(req.Spec, req.DashboardFilters, ct));

    /// <summary>Execute a saved widget by id (reads its stored configuration).</summary>
    [HttpPost("{widgetId:guid}/execute")]
    [RequirePermission("Platform.Dashboards.View")]
    public async Task<ActionResult<ApiResponse<WidgetDataResult>>> Execute(Guid widgetId, [FromBody] ExecuteRequest? req, CancellationToken ct)
        => OkResponse(await _data.ExecuteWidgetAsync(widgetId, req?.DashboardFilters, ct));

    /// <summary>Drill-down: the detail rows behind a widget, optionally for one clicked segment.</summary>
    [HttpPost("drilldown")]
    [RequirePermission("Platform.Dashboards.View")]
    public async Task<ActionResult<ApiResponse<WidgetDataResult>>> Drilldown([FromBody] DrilldownRequest req, CancellationToken ct)
        => OkResponse(await _data.GetRowsAsync(req.Spec, req.SegmentKey, req.DashboardFilters, req.Page ?? 1, req.PageSize ?? 25, ct));

    /// <summary>Export a saved widget's data as a real Excel/PDF/CSV file.</summary>
    [HttpGet("{widgetId:guid}/export")]
    [RequirePermission("Platform.Dashboards.View")]
    public async Task<IActionResult> Export(Guid widgetId, [FromQuery] string format = "excel", CancellationToken ct = default)
    {
        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var fmt))
            return BadRequest(ApiResponse.Fail($"Unknown export format '{format}'. Use excel, csv, or pdf."));
        var file = await _export.ExportAsync(widgetId, fmt, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}

public sealed class AiSuggestRequest
{
    public string? Prompt { get; set; }
}

public sealed class PreviewRequest
{
    public WidgetQuerySpec Spec { get; set; } = null!;
    public List<WidgetFilterSpec>? DashboardFilters { get; set; }
}

public sealed class ExecuteRequest
{
    public List<WidgetFilterSpec>? DashboardFilters { get; set; }
}

public sealed class DrilldownRequest
{
    public WidgetQuerySpec Spec { get; set; } = null!;
    public string? SegmentKey { get; set; }
    public List<WidgetFilterSpec>? DashboardFilters { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}
