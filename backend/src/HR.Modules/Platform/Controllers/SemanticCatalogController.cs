using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Application.SemanticCatalog;
using HR.Application.SemanticCatalog.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

/// <summary>
/// Read-only Semantic Catalog API — exposes permission-filtered domains, objects,
/// metrics, and full-text search. Powers the AI assistant and widget builder.
/// Responses use the standard ApiResponse envelope (unwrapped by the frontend apiFetch).
/// </summary>
[Authorize]
[Route("api/platform/catalog")]
public sealed class SemanticCatalogController : BaseApiController
{
    private readonly ISemanticCatalogProvider _catalog;
    private readonly ICurrentUserService _user;

    public SemanticCatalogController(ISemanticCatalogProvider catalog, ICurrentUserService user)
    {
        _catalog = catalog;
        _user = user;
    }

    private CatalogQueryContext Ctx => new(_user.Permissions);

    [HttpGet("domains")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<IReadOnlyList<SemanticDomain>>> GetDomains()
        => OkResponse(_catalog.GetDomains(Ctx));

    [HttpGet("objects")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<IReadOnlyList<SemanticObject>>> GetObjects([FromQuery] string? domain)
        => OkResponse(_catalog.GetObjects(Ctx, domain));

    [HttpGet("objects/{objectCode}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<SemanticObject>> GetObject(string objectCode)
    {
        var o = _catalog.GetObject(Ctx, objectCode);
        if (o is null) return NotFound(ApiResponse.Fail($"Object '{objectCode}' not found"));
        return OkResponse(o);
    }

    [HttpGet("metrics")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<IReadOnlyList<SemanticMetric>>> GetMetrics([FromQuery] string? domain)
        => OkResponse(_catalog.GetMetrics(Ctx, domain));

    [HttpGet("metrics/{metricCode}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<SemanticMetric>> GetMetric(string metricCode)
    {
        var m = _catalog.GetMetric(Ctx, metricCode);
        if (m is null) return NotFound(ApiResponse.Fail($"Metric '{metricCode}' not found"));
        return OkResponse(m);
    }

    [HttpGet("search")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<IReadOnlyList<SemanticSearchHit>>> Search([FromQuery] string? q)
        => OkResponse(_catalog.Search(Ctx, q ?? ""));

    [HttpGet("health")]
    [RequirePermission("Platform.Dashboards.Create")]
    public ActionResult<ApiResponse<CatalogHealth>> Health()
        => OkResponse(_catalog.GetHealth());
}
