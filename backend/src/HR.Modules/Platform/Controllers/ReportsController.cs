using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Models;
using HR.Modules.Platform.Commands.Reports;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Queries.Reports;
using HR.Modules.Platform.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Modules.Platform.Controllers;

[Authorize]
[Route("api/platform/reports")]
public class ReportsController : BaseApiController
{
    [HttpGet]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<PaginatedList<ReportDefinitionDto>>>> GetAll([FromQuery] GetReportsQuery query, CancellationToken ct)
    { var result = await Mediator.Send(query, ct); return OkResponse(result); }

    [HttpGet("{id:guid}")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<ReportDefinitionDto>>> GetById(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new GetReportByIdQuery(id), ct); return OkResponse(result); }

    [HttpPost]
    [RequirePermission("Platform.Reports.Create")]
    public async Task<ActionResult<ApiResponse<ReportDefinitionDto>>> Create([FromBody] CreateReportCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command, ct); return CreatedResponse(result); }

    [HttpPut("{id:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportDefinitionDto>>> Update(Guid id, [FromBody] UpdateReportCommand command, CancellationToken ct)
    { if (id != command.Id) return BadRequest(); var result = await Mediator.Send(command, ct); return OkResponse(result); }

    [HttpDelete("{id:guid}")]
    [RequirePermission("Platform.Reports.Delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    { await Mediator.Send(new DeleteReportCommand(id), ct); return OkResponse("Report deleted"); }

    // Body is optional: the pre-parameter callers post /run with no body at all.
    [HttpPost("{id:guid}/run")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<ReportResult>>> Run(Guid id, [FromBody] RunReportRequest? request = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    { var result = await Mediator.Send(new RunReportQuery(id, page, pageSize, request?.Parameters), ct); return OkResponse(result); }

    // Export is a GET (the browser navigates to it for the download), so parameters ride the query
    // string as p.<fieldCode>=<value> rather than a body. Without them an export of a parameterized
    // report would quietly fall back to stored defaults and disagree with the table on screen.
    [HttpGet("{id:guid}/export")]
    [RequirePermission("Platform.Reports.Export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "excel", CancellationToken ct = default)
    {
        var file = await Mediator.Send(new ExportReportQuery(id, format, ReadParameterQuery()), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    private const string ParameterQueryPrefix = "p.";

    private IReadOnlyDictionary<string, string?>? ReadParameterQuery()
    {
        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Request.Query)
        {
            if (kv.Key.StartsWith(ParameterQueryPrefix, StringComparison.OrdinalIgnoreCase))
                parameters[kv.Key[ParameterQueryPrefix.Length..]] = kv.Value.ToString();
        }
        return parameters.Count > 0 ? parameters : null;
    }

    [HttpPost("{id:guid}/publish")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportDefinitionDto>>> Publish(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new PublishReportCommand(id), ct); return OkResponse(result); }

    [HttpPost("{id:guid}/clone")]
    [RequirePermission("Platform.Reports.Create")]
    public async Task<ActionResult<ApiResponse<ReportDefinitionDto>>> Clone(Guid id, [FromBody] CloneReportCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { SourceReportId = id }, ct); return CreatedResponse(result); }

    // Fields
    [HttpPost("{id:guid}/fields")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportFieldDto>>> AddField(Guid id, [FromBody] AddReportFieldCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("fields/{fieldId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteField(Guid fieldId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportFieldCommand(fieldId), ct); return OkResponse("Field removed"); }

    // Filters
    [HttpPost("{id:guid}/filters")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportFilterDto>>> AddFilter(Guid id, [FromBody] AddReportFilterCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("filters/{filterId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteFilter(Guid filterId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportFilterCommand(filterId), ct); return OkResponse("Filter removed"); }

    // Groupings
    [HttpPost("{id:guid}/groupings")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportGroupingDto>>> AddGrouping(Guid id, [FromBody] AddReportGroupingCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("groupings/{groupingId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteGrouping(Guid groupingId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportGroupingCommand(groupingId), ct); return OkResponse("Grouping removed"); }

    // Sortings
    [HttpPost("{id:guid}/sortings")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportSortingDto>>> AddSorting(Guid id, [FromBody] AddReportSortingCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("sortings/{sortingId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteSorting(Guid sortingId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportSortingCommand(sortingId), ct); return OkResponse("Sorting removed"); }

    // Formula validation — pure, no DB, so the reports builder AND the dashboard widget builder
    // can validate as the user types (widened to Dashboards.View for the widget Calculated mode).
    [HttpPost("validate-formula")]
    [RequirePermission("Platform.Reports.Edit", "Platform.Dashboards.View")]
    public ActionResult<ApiResponse<FormulaValidationDto>> ValidateFormula([FromBody] ValidateFormulaRequest request)
    {
        var error = ReportFormulaCompiler.Validate(request.Formula);
        return OkResponse(new FormulaValidationDto { IsValid = error is null, Error = error });
    }

    // Relationships (joins)
    [HttpGet("{id:guid}/relationships")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportRelationshipDto>>>> GetRelationships(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new GetReportRelationshipsQuery(id), ct); return OkResponse(result); }

    [HttpPost("{id:guid}/relationships")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportRelationshipDto>>> AddRelationship(Guid id, [FromBody] AddReportRelationshipCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("relationships/{relationshipId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteRelationship(Guid relationshipId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportRelationshipCommand(relationshipId), ct); return OkResponse("Relationship removed"); }

    // Schedules
    [HttpGet("{id:guid}/schedules")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportScheduleDto>>>> GetSchedules(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new GetReportSchedulesQuery(id), ct); return OkResponse(result); }

    [HttpPost("{id:guid}/schedules")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportScheduleDto>>> AddSchedule(Guid id, [FromBody] AddReportScheduleCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("schedules/{scheduleId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteSchedule(Guid scheduleId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportScheduleCommand(scheduleId), ct); return OkResponse("Schedule removed"); }

    // Shares
    [HttpGet("{id:guid}/shares")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportShareDto>>>> GetShares(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new GetReportSharesQuery(id), ct); return OkResponse(result); }

    [HttpPost("{id:guid}/shares")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportShareDto>>> AddShare(Guid id, [FromBody] AddReportShareCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("{id:guid}/shares/{shareId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> RemoveShare(Guid id, Guid shareId, CancellationToken ct)
    { await Mediator.Send(new RemoveReportShareCommand(id, shareId), ct); return OkResponse("Share removed"); }

    // Templates
    [HttpGet("templates")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportTemplateDto>>>> GetTemplates(CancellationToken ct)
    { var result = await Mediator.Send(new GetReportTemplatesQuery(), ct); return OkResponse(result); }

    [HttpPost("templates")]
    [RequirePermission("Platform.Reports.Create")]
    public async Task<ActionResult<ApiResponse<ReportTemplateDto>>> CreateTemplate([FromBody] CreateReportTemplateCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command, ct); return CreatedResponse(result); }

    // Folders
    [HttpGet("folders")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportFolderDto>>>> GetFolders(CancellationToken ct)
    { var result = await Mediator.Send(new GetReportFoldersQuery(), ct); return OkResponse(result); }

    [HttpPost("folders")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportFolderDto>>> CreateFolder([FromBody] CreateReportFolderCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command, ct); return CreatedResponse(result); }

    [HttpPut("folders/{folderId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportFolderDto>>> UpdateFolder(Guid folderId, [FromBody] UpdateReportFolderCommand command, CancellationToken ct)
    { if (folderId != command.Id) return BadRequest(); var result = await Mediator.Send(command, ct); return OkResponse(result); }

    [HttpDelete("folders/{folderId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteFolder(Guid folderId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportFolderCommand(folderId), ct); return OkResponse("Folder deleted"); }

    [HttpPut("{id:guid}/folder")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> SetFolder(Guid id, [FromBody] SetReportFolderCommand command, CancellationToken ct)
    { await Mediator.Send(command with { ReportDefinitionId = id }, ct); return OkResponse("Report folder updated"); }

    // Tags
    [HttpGet("tags")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportTagDto>>>> GetTags(CancellationToken ct)
    { var result = await Mediator.Send(new GetReportTagsQuery(), ct); return OkResponse(result); }

    [HttpPost("tags")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportTagDto>>> CreateTag([FromBody] CreateReportTagCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command, ct); return CreatedResponse(result); }

    [HttpDelete("tags/{tagId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteTag(Guid tagId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportTagCommand(tagId), ct); return OkResponse("Tag deleted"); }

    [HttpPost("{id:guid}/tags/{tagId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> AssignTag(Guid id, Guid tagId, CancellationToken ct)
    { await Mediator.Send(new AssignReportTagCommand(id, tagId), ct); return OkResponse("Tag assigned"); }

    [HttpDelete("{id:guid}/tags/{tagId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> UnassignTag(Guid id, Guid tagId, CancellationToken ct)
    { await Mediator.Send(new UnassignReportTagCommand(id, tagId), ct); return OkResponse("Tag unassigned"); }

    // User State
    [HttpPost("{id:guid}/favorite")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleFavorite(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new ToggleReportFavoriteCommand(id), ct); return OkResponse(result); }

    [HttpPost("{id:guid}/pin")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<bool>>> TogglePin(Guid id, CancellationToken ct)
    { var result = await Mediator.Send(new ToggleReportPinCommand(id), ct); return OkResponse(result); }
}
