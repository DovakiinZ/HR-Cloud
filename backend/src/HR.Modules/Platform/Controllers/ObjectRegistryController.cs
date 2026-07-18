using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Models;
using HR.Modules.Platform.Commands.ObjectRegistry;
using HR.Modules.Platform.DTOs.ObjectRegistry;
using HR.Modules.Platform.Queries.ObjectRegistry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

[Authorize]
[Route("api/platform/objects")]
public class ObjectRegistryController : BaseApiController
{
    [HttpGet]
    [RequirePermission("Platform.Objects.View", "Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ObjectDefinitionDto>>>> GetAll(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetObjectDefinitionsQuery(), ct);
        return OkResponse(result);
    }

    [HttpGet("{code}")]
    [RequirePermission("Platform.Objects.View", "Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<ObjectDefinitionDto>>> GetByCode(string code, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetObjectDefinitionByCodeQuery(code), ct);
        return OkResponse(result);
    }

    [HttpPost]
    [RequirePermission("Platform.Objects.Create")]
    public async Task<ActionResult<ApiResponse<ObjectDefinitionDto>>> Create(
        [FromBody] CreateObjectDefinitionCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return CreatedResponse(result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("Platform.Objects.Edit")]
    public async Task<ActionResult<ApiResponse<ObjectDefinitionDto>>> Update(
        Guid id, [FromBody] UpdateObjectDefinitionCommand command, CancellationToken ct)
    {
        if (id != command.Id) return BadRequest();
        var result = await Mediator.Send(command, ct);
        return OkResponse(result);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("Platform.Objects.Delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    {
        await Mediator.Send(new DeleteObjectDefinitionCommand(id), ct);
        return OkResponse("Object definition deleted");
    }

    /// <summary>
    /// Idempotent seed — registers the five main HR entities (Employee, AttendanceRecord,
    /// PayrollPayslip, LeaveBalance, RequestInstance) as ObjectDefinition rows so the
    /// report engine and field registry can target them.  Safe to call multiple times.
    /// </summary>
    [HttpPost("seed-reportable")]
    [RequirePermission("Platform.Objects.Create")]
    public async Task<ActionResult<ApiResponse<int>>> SeedReportable(CancellationToken ct)
        => OkResponse(await Mediator.Send(new RegisterReportableObjectsCommand(), ct));

    // Field endpoints
    [HttpPost("{id:guid}/fields")]
    [RequirePermission("Platform.Objects.Edit")]
    public async Task<ActionResult<ApiResponse<ObjectFieldDto>>> AddField(
        Guid id, [FromBody] AddObjectFieldCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { ObjectDefinitionId = id }, ct);
        return CreatedResponse(result);
    }

    [HttpPut("{id:guid}/fields/{fieldId:guid}")]
    [RequirePermission("Platform.Objects.Edit")]
    public async Task<ActionResult<ApiResponse<ObjectFieldDto>>> UpdateField(
        Guid id, Guid fieldId, [FromBody] UpdateObjectFieldCommand command, CancellationToken ct)
    {
        if (fieldId != command.Id) return BadRequest();
        var result = await Mediator.Send(command, ct);
        return OkResponse(result);
    }

    [HttpDelete("{id:guid}/fields/{fieldId:guid}")]
    [RequirePermission("Platform.Objects.Delete")]
    public async Task<ActionResult<ApiResponse>> DeleteField(Guid id, Guid fieldId, CancellationToken ct)
    {
        await Mediator.Send(new DeleteObjectFieldCommand(fieldId), ct);
        return OkResponse("Field deleted");
    }

    // Relationship endpoints
    [HttpPost("{id:guid}/relationships")]
    [RequirePermission("Platform.Objects.Edit")]
    public async Task<ActionResult<ApiResponse<ObjectRelationshipDto>>> AddRelationship(
        Guid id, [FromBody] AddObjectRelationshipCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { SourceObjectId = id }, ct);
        return CreatedResponse(result);
    }

    [HttpDelete("{id:guid}/relationships/{relId:guid}")]
    [RequirePermission("Platform.Objects.Delete")]
    public async Task<ActionResult<ApiResponse>> DeleteRelationship(Guid id, Guid relId, CancellationToken ct)
    {
        await Mediator.Send(new DeleteObjectRelationshipCommand(relId), ct);
        return OkResponse("Relationship deleted");
    }

    // Permission endpoints
    [HttpPost("{id:guid}/permissions")]
    [RequirePermission("Platform.Objects.Edit")]
    public async Task<ActionResult<ApiResponse<Guid>>> AddPermission(
        Guid id, [FromBody] AddObjectPermissionCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { ObjectDefinitionId = id }, ct);
        return CreatedResponse(result);
    }

    [HttpDelete("{id:guid}/permissions/{permId:guid}")]
    [RequirePermission("Platform.Objects.Delete")]
    public async Task<ActionResult<ApiResponse>> DeletePermission(Guid id, Guid permId, CancellationToken ct)
    {
        await Mediator.Send(new DeleteObjectPermissionCommand(permId), ct);
        return OkResponse("Permission deleted");
    }
}
