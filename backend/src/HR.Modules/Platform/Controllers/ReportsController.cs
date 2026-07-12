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

    [HttpPost("{id:guid}/run")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<ReportResult>>> Run(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    { var result = await Mediator.Send(new RunReportQuery(id, page, pageSize), ct); return OkResponse(result); }

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

    // Schedules
    [HttpPost("{id:guid}/schedules")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse<ReportScheduleDto>>> AddSchedule(Guid id, [FromBody] AddReportScheduleCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

    [HttpDelete("schedules/{scheduleId:guid}")]
    [RequirePermission("Platform.Reports.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteSchedule(Guid scheduleId, CancellationToken ct)
    { await Mediator.Send(new DeleteReportScheduleCommand(scheduleId), ct); return OkResponse("Schedule removed"); }

    // Templates
    [HttpGet("templates")]
    [RequirePermission("Platform.Reports.View")]
    public async Task<ActionResult<ApiResponse<List<ReportTemplateDto>>>> GetTemplates(CancellationToken ct)
    { var result = await Mediator.Send(new GetReportTemplatesQuery(), ct); return OkResponse(result); }

    [HttpPost("templates")]
    [RequirePermission("Platform.Reports.Create")]
    public async Task<ActionResult<ApiResponse<ReportTemplateDto>>> CreateTemplate([FromBody] CreateReportTemplateCommand command, CancellationToken ct)
    { var result = await Mediator.Send(command, ct); return CreatedResponse(result); }
}
