using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Interfaces;
using HR.Application.SemanticCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

/// <summary>
/// Read-only Semantic Catalog API — exposes permission-filtered domains, objects,
/// metrics, and full-text search. Powers the AI assistant and widget builder.
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
    public IActionResult GetDomains() => Ok(_catalog.GetDomains(Ctx));

    [HttpGet("objects")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetObjects([FromQuery] string? domain) => Ok(_catalog.GetObjects(Ctx, domain));

    [HttpGet("objects/{objectCode}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetObject(string objectCode)
        => _catalog.GetObject(Ctx, objectCode) is { } o ? Ok(o) : NotFound();

    [HttpGet("metrics")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetMetrics([FromQuery] string? domain) => Ok(_catalog.GetMetrics(Ctx, domain));

    [HttpGet("metrics/{metricCode}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult GetMetric(string metricCode)
        => _catalog.GetMetric(Ctx, metricCode) is { } m ? Ok(m) : NotFound();

    [HttpGet("search")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public IActionResult Search([FromQuery] string? q) => Ok(_catalog.Search(Ctx, q ?? ""));

    [HttpGet("health")]
    [RequirePermission("Platform.Dashboards.Create")]
    public IActionResult Health() => Ok(_catalog.GetHealth());
}
