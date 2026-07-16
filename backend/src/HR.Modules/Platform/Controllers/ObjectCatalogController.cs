using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Models;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

/// <summary>
/// Exposes the Object / Property Registry catalog (discovered live from the data model)
/// that powers the Widget Builder. Future objects appear here automatically.
/// </summary>
[Authorize]
[Route("api/platform/registry")]
public class ObjectCatalogController : BaseApiController
{
    private readonly IObjectCatalogService _catalog;
    public ObjectCatalogController(IObjectCatalogService catalog) => _catalog = catalog;

    [HttpGet("objects")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<List<CatalogObjectDto>>> GetObjects()
        => OkResponse(_catalog.GetCatalog().ToList());

    [HttpGet("objects/{code}")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<CatalogObjectDto>> GetObject(string code)
    {
        var obj = _catalog.GetObject(code);
        return obj is null ? NotFound(ApiResponse.Fail($"Object '{code}' not found")) : OkResponse(obj);
    }

    [HttpGet("objects/{code}/fields")]
    [RequirePermission("Platform.Dashboards.View", "Platform.Reports.View")]
    public ActionResult<ApiResponse<List<CatalogFieldDto>>> GetFields(string code)
    {
        var obj = _catalog.GetObject(code);
        return obj is null ? NotFound(ApiResponse.Fail($"Object '{code}' not found")) : OkResponse(obj.Fields);
    }
}
