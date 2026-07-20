using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Models;
using HR.Application.Engines.Completion;
using HR.Modules.Platform.DTOs.Requests;
using HR.Modules.Platform.Services.Assets;
using HR.Modules.Platform.Services.Completion;
using HR.Modules.Platform.Services.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

/// <summary>
/// Request-type and effect-definition administration.
///
/// Deliberately thin: every rule — system-request protection, required-effect protection, code
/// immutability, activation gating, tenant scoping — lives in the services and validators. The
/// actions here route, name a permission, and project. A rule implemented in a controller is a rule
/// the provisioning path and the tests do not share.
///
/// Errors map through the existing middleware: ValidationException → 400, ForbiddenException → 403,
/// NotFoundException → 404, ConflictException → 409. A cross-tenant id surfaces as 404 because the
/// global query filter simply does not resolve it.
/// </summary>
[Authorize]
[Route("api/platform/request-types")]
public class RequestTypesController : BaseApiController
{
    private readonly IRequestTypeAdminService _types;
    private readonly IRequestEffectDefinitionService _effects;
    private readonly IEffectActionCatalog _catalog;
    private readonly IEffectExecutorRegistry _executors;
    private readonly IAssetLookupService _assets;
    private readonly IEffectPermissionGuard _permissions;

    public RequestTypesController(
        IRequestTypeAdminService types,
        IRequestEffectDefinitionService effects,
        IEffectActionCatalog catalog,
        IEffectExecutorRegistry executors,
        IAssetLookupService assets,
        IEffectPermissionGuard permissions)
    { _types = types; _effects = effects; _catalog = catalog; _executors = executors; _assets = assets; _permissions = permissions; }

    // ── Request type CRUD ─────────────────────────────────────────────────────

    [HttpGet]
    [RequirePermission("Platform.Workflows.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RequestTypeListItemDto>>>> List(
        [FromQuery] bool includeInactive = true, CancellationToken ct = default)
        => OkResponse(await _types.ListAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    [RequirePermission("Platform.Workflows.View")]
    public async Task<ActionResult<ApiResponse<RequestTypeDetailAdminDto>>> Get(Guid id, CancellationToken ct)
        => OkResponse(await _types.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission("Platform.Workflows.Create")]
    public async Task<ActionResult<ApiResponse<RequestTypeDetailAdminDto>>> Create(
        [FromBody] CreateRequestTypeInput input, CancellationToken ct)
        => CreatedResponse(await _types.CreateAsync(input, ct));

    [HttpPut("{id:guid}")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse<RequestTypeDetailAdminDto>>> Update(
        Guid id, [FromBody] UpdateRequestTypeInput input, CancellationToken ct)
        => OkResponse(await _types.UpdateAsync(id, input, ct));

    /// <summary>Refused with 403 for a system request type — see RequestTypeAdminService.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("Platform.Workflows.Delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    {
        await _types.DeleteAsync(id, ct);
        return OkResponse("Request type deleted");
    }

    /// <summary>Copy a request type. A copy of a system request is always tenant-owned and custom.</summary>
    [HttpPost("{id:guid}/duplicate")]
    [RequirePermission("Platform.Workflows.Create")]
    public async Task<ActionResult<ApiResponse<RequestTypeDetailAdminDto>>> Duplicate(
        Guid id, [FromBody] DuplicateRequestTypeInput input, CancellationToken ct)
        => CreatedResponse(await _types.DuplicateAsync(id, input, ct));

    // ── Validation + activation ───────────────────────────────────────────────

    [HttpGet("{id:guid}/validate")]
    [RequirePermission("Platform.Workflows.View")]
    public async Task<ActionResult<ApiResponse<ValidationResultDto>>> Validate(Guid id, CancellationToken ct)
        => OkResponse(await _types.ValidateAsync(id, ct));

    /// <summary>Activation runs the full validation and fails with 400 listing every problem.</summary>
    [HttpPut("{id:guid}/active")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse<RequestTypeDetailAdminDto>>> SetActive(
        Guid id, [FromBody] SetActiveInput input, CancellationToken ct)
        => OkResponse(await _types.SetActiveAsync(id, input.IsActive, ct));

    // ── Effect definitions ────────────────────────────────────────────────────

    [HttpGet("{id:guid}/effects")]
    [RequirePermission("Platform.Workflows.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RequestEffectDefinitionDto>>>> ListEffects(
        Guid id, CancellationToken ct)
        => OkResponse(await _effects.ListAsync(id, ct));

    [HttpPost("{id:guid}/effects")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse<RequestEffectDefinitionDto>>> AddEffect(
        Guid id, [FromBody] UpsertEffectDefinitionInput input, CancellationToken ct)
        => CreatedResponse(await _effects.AddAsync(id, input, ct));

    /// <summary>Dry-run a configuration against the linked form — what the builder calls as inputs
    /// are mapped, so a user sees the problem before saving.</summary>
    [HttpPost("{id:guid}/effects/validate")]
    [RequirePermission("Platform.Workflows.View")]
    public async Task<ActionResult<ApiResponse<ValidationResultDto>>> ValidateEffect(
        Guid id, [FromBody] UpsertEffectDefinitionInput input, CancellationToken ct)
        => OkResponse(await _effects.ValidateAsync(id, input, ct));

    [HttpPut("{id:guid}/effects/reorder")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RequestEffectDefinitionDto>>>> ReorderEffects(
        Guid id, [FromBody] ReorderEffectsInput input, CancellationToken ct)
        => OkResponse(await _effects.ReorderAsync(id, input, ct));

    [HttpPut("effects/{effectId:guid}")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse<RequestEffectDefinitionDto>>> UpdateEffect(
        Guid effectId, [FromBody] UpsertEffectDefinitionInput input, CancellationToken ct)
        => OkResponse(await _effects.UpdateAsync(effectId, input, ct));

    /// <summary>Refused with 403 for a required effect.</summary>
    [HttpPut("effects/{effectId:guid}/enabled")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse<RequestEffectDefinitionDto>>> SetEffectEnabled(
        Guid effectId, [FromBody] SetEnabledInput input, CancellationToken ct)
        => OkResponse(await _effects.SetEnabledAsync(effectId, input.IsEnabled, ct));

    /// <summary>Refused with 403 for a required effect.</summary>
    [HttpDelete("effects/{effectId:guid}")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteEffect(Guid effectId, CancellationToken ct)
    {
        await _effects.DeleteAsync(effectId, ct);
        return OkResponse("Effect removed");
    }

    // ── Effect Action Catalog ─────────────────────────────────────────────────

    /// <summary>
    /// The trusted vocabulary the builder may configure. ExecutorAvailable is projected per action
    /// so the UI can grey out anything that would fail at approval time rather than offering it.
    /// </summary>
    [HttpGet("~/api/platform/effect-catalog")]
    [RequirePermission("Platform.Workflows.View")]
    public ActionResult<ApiResponse<IReadOnlyList<EffectActionDto>>> Catalog()
    {
        // Only what this caller may actually configure. Offering an action the service would then
        // refuse turns a permission boundary into a confusing error at save time.
        var actions = _permissions.ConfigurableActions().Select(d => new EffectActionDto
        {
            EffectType = d.EffectType,
            LabelAr = d.LabelAr, LabelEn = d.LabelEn,
            DescriptionAr = d.DescriptionAr, DescriptionEn = d.DescriptionEn,
            Module = d.Module,
            SupportedTriggers = d.SupportedTriggers.Select(t => t.ToString()).ToList(),
            ExecutionMode = d.ExecutionMode.ToString(),
            Inputs = d.Inputs.Select(i => new EffectInputDto
            {
                Key = i.Key, LabelAr = i.LabelAr, LabelEn = i.LabelEn, IsRequired = i.IsRequired,
                AllowedSources = i.AllowedSources.Select(s => s.ToString()).ToList(),
            }).ToList(),
            RequiredPermissions = d.RequiredPermissions,
            ExecutorAvailable = _executors.TryResolve(d.EffectType, out _),
        }).ToList();

        return OkResponse<IReadOnlyList<EffectActionDto>>(actions);
    }

    // ── Asset lookup (Custody Request's asset picker) ─────────────────────────

    /// <summary>
    /// Assets that can actually be assigned: active, and with no open custody. Replaces the
    /// AssetType master-data lookup the seeded custody form originally pointed at, which listed
    /// asset *categories* rather than assets.
    /// </summary>
    [HttpGet("~/api/platform/assets/assignable")]
    [RequirePermission("Platform.MasterData.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AssignableAssetDto>>>> AssignableAssets(
        [FromQuery] string? search, CancellationToken ct)
        => OkResponse(await _assets.GetAssignableAsync(search, ct));

    [HttpGet("~/api/platform/assets")]
    [RequirePermission("Platform.MasterData.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AssignableAssetDto>>>> AllAssets(
        [FromQuery] string? search, CancellationToken ct)
        => OkResponse(await _assets.GetAllAsync(search, ct));
}

public sealed record SetActiveInput(bool IsActive);
public sealed record SetEnabledInput(bool IsEnabled);
