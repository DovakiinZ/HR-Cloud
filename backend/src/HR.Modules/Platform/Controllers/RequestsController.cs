using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Platform.Controllers;

/// <summary>
/// Employee-facing Request Center. Only fully-provisioned request types are exposed,
/// so "if visible, it is usable". Authorization is enforced by the engine (employee
/// ownership / approver assignment), keeping endpoints simple and safe.
/// </summary>
[Authorize]
[Route("api/requests")]
public class RequestsController : BaseApiController
{
    private readonly IRequestEngine _engine;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ILeaveService _leave;

    public RequestsController(IRequestEngine engine, ApplicationDbContext db, ICurrentUserService user, ILeaveService leave)
    {
        _engine = engine; _db = db; _user = user; _leave = leave;
    }

    // ── Leave (generic sub-typed request) ───────────────────────────────────────

    /// <summary>Leave types from settings, with their rules and the employee's live balance.</summary>
    [HttpGet("leave-types")]
    public async Task<ActionResult<ApiResponse<List<LeaveTypeInfo>>>> GetLeaveTypes(CancellationToken ct)
    {
        var empId = await _db.Employees.Where(e => e.UserId == _user.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (empId is null) return OkResponse(new List<LeaveTypeInfo>());
        return OkResponse(await _leave.GetLeaveTypesAsync(empId.Value, ct));
    }

    /// <summary>Admin: an employee's leave types with their balances (entitled/used/remaining).</summary>
    [HttpGet("admin/leave-balances")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Edit")]
    public async Task<ActionResult<ApiResponse<List<LeaveTypeInfo>>>> AdminLeaveBalances([FromQuery] Guid employeeId, CancellationToken ct)
        => OkResponse(await _leave.GetLeaveTypesAsync(employeeId, ct));

    /// <summary>Admin: set an employee's entitled / carried-forward balance for a leave type.</summary>
    [HttpPut("admin/leave-balances")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Edit")]
    public async Task<ActionResult<ApiResponse>> SetLeaveBalance([FromBody] SetLeaveBalanceBody body, CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var bal = await _db.LeaveBalances.FirstOrDefaultAsync(
            b => b.EmployeeId == body.EmployeeId && b.LeaveTypeId == body.LeaveTypeId && b.Year == year, ct);
        if (bal is null)
        {
            bal = new HR.Domain.Engines.Leave.LeaveBalance { EmployeeId = body.EmployeeId, LeaveTypeId = body.LeaveTypeId, Year = year };
            _db.LeaveBalances.Add(bal);
        }
        bal.EntitledDays = body.EntitledDays;
        bal.CarriedForwardDays = body.CarriedForwardDays;
        await _db.SaveChangesAsync(ct);
        return OkResponse("Balance updated");
    }

    /// <summary>Live preview for the leave wizard: days, balance before/after, next approver, validation.</summary>
    [HttpPost("leave/preview")]
    public async Task<ActionResult<ApiResponse<LeavePreview>>> LeavePreview([FromBody] LeavePreviewBody body, CancellationToken ct)
    {
        var empId = await _db.Employees.Where(e => e.UserId == _user.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (empId is null) return OkResponse(new LeavePreview { Errors = { "لا يوجد ملف موظف مرتبط بحسابك" } });
        DateTime? Parse(string? s) => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        return OkResponse(await _leave.PreviewAsync(empId.Value, body.LeaveTypeId, Parse(body.StartDate), Parse(body.EndDate), body.HasAttachment, ct));
    }

    /// <summary>Idempotently provision the built-in system requests for this tenant.</summary>
    /// <summary>
    /// Bring this tenant's built-in request types up to the current seed version: create what is
    /// missing, upgrade what is behind, touch nothing a tenant has customised.
    ///
    /// Idempotent and safe to re-run — this is also the recovery path when the onboarding hook
    /// failed. Runs in its own tenant execution scope rather than relying on the caller's, so the
    /// same code path serves onboarding, manual provisioning and background upgrades.
    /// </summary>
    [HttpPost("provision")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Create")]
    public async Task<ActionResult<ApiResponse<RequestProvisioningResult>>> Provision(CancellationToken ct)
    {
        var svc = HttpContext.RequestServices.GetRequiredService<IRequestProvisioningService>();
        var user = HttpContext.RequestServices.GetRequiredService<HR.Application.Common.Interfaces.ICurrentUserService>();
        var result = await svc.ProvisionTenantAsync(user.TenantId, user.UserId, ct);
        return OkResponse(result,
            $"{result.Created} created, {result.Upgraded} upgraded, {result.AlreadyCurrent} already current");
    }

    [HttpPost("seed-system")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Create")]
    public async Task<ActionResult<ApiResponse<int>>> SeedSystem(CancellationToken ct)
    {
        var seeder = HttpContext.RequestServices.GetRequiredService<IRequestSeeder>();
        var n = await seeder.SeedSystemRequestsAsync(ct);
        return OkResponse(n, "System requests provisioned");
    }

    // ── Catalog (usable request types) ──────────────────────────────────────────

    [HttpGet("types")]
    public async Task<ActionResult<ApiResponse<List<RequestTypeDto>>>> GetTypes(CancellationToken ct)
    {
        var types = await _db.RequestTypes
            .Where(t => t.IsActive && t.FormDefinitionId != Guid.Empty)
            .OrderBy(t => t.Kind).ThenBy(t => t.SortOrder).ThenBy(t => t.NameAr)
            .Select(t => new RequestTypeDto
            {
                Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn,
                DescriptionAr = t.DescriptionAr, DescriptionEn = t.DescriptionEn,
                Kind = t.Kind.ToString(), CategoryId = t.CategoryId,
                Icon = t.Icon, Color = t.Color, IsSystem = t.IsSystem,
                HasWorkflow = t.WorkflowDefinitionId != null, GeneratesDocument = t.PrintTemplateId != null,
            }).ToListAsync(ct);
        return OkResponse(types);
    }

    [HttpGet("types/{id:guid}")]
    public async Task<ActionResult<ApiResponse<RequestTypeDetailDto>>> GetType(Guid id, CancellationToken ct)
    {
        var t = await _db.RequestTypes.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (t is null) return NotFound(ApiResponse.Fail("Request type not found"));

        var fields = await _db.FormFields
            .Where(f => f.FormDefinitionId == t.FormDefinitionId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new RequestFieldDto
            {
                Id = f.Id, Code = f.Code, NameAr = f.NameAr, NameEn = f.NameEn,
                FieldType = f.FieldType.ToString(), IsRequired = f.IsRequired,
                Placeholder = f.Placeholder, Options = f.Options, SortOrder = f.SortOrder,
            }).ToListAsync(ct);

        var isLeave = await _db.RequestImpactMappings.AnyAsync(m => m.RequestTypeId == id && m.AffectsLeaveBalance, ct);
        return OkResponse(new RequestTypeDetailDto
        {
            Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn,
            DescriptionAr = t.DescriptionAr, DescriptionEn = t.DescriptionEn,
            Kind = t.Kind.ToString(), FormDefinitionId = t.FormDefinitionId,
            Icon = t.Icon, Color = t.Color, Fields = fields, IsLeaveRequest = isLeave,
        });
    }

    // ── Admin: bind a print template to a request type (entity-level — drives PDF output) ──

    /// <summary>All request types with workflow + default-template assignment and an activation-readiness flag (admin).</summary>
    [HttpGet("types/admin")]
    [RequirePermission("Platform.Documents.View")]
    public async Task<ActionResult<ApiResponse<List<RequestTypeAdminDto>>>> GetTypesAdmin(CancellationToken ct)
    {
        var types = await (from t in _db.RequestTypes
                           join wf in _db.WorkflowDefinitions on t.WorkflowDefinitionId equals wf.Id into wfj
                           from wf in wfj.DefaultIfEmpty()
                           join dt in _db.DocumentTemplates on t.PrintTemplateId equals dt.Id into dtj
                           from dt in dtj.DefaultIfEmpty()
                           orderby t.SortOrder, t.NameAr
                           select new RequestTypeAdminDto
                           {
                               Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn,
                               CategoryId = t.CategoryId, IsActive = t.IsActive, IsSystem = t.IsSystem,
                               FormDefinitionId = t.FormDefinitionId,
                               WorkflowDefinitionId = t.WorkflowDefinitionId,
                               WorkflowName = wf != null ? wf.NameAr : null,
                               PrintTemplateId = t.PrintTemplateId,
                               PrintTemplateName = dt != null ? dt.NameAr : null,
                               // Ready to activate when it has a form and an approval workflow assigned.
                               ActivationReady = t.FormDefinitionId != Guid.Empty && t.WorkflowDefinitionId != null,
                           }).ToListAsync(ct);
        return OkResponse(types);
    }

    /// <summary>Assign (or clear) the approval workflow a request type runs — writes the entity FK the engine reads.</summary>
    [HttpPut("types/{id:guid}/workflow")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse>> SetWorkflow(Guid id, [FromBody] SetWorkflowBody body, CancellationToken ct)
    {
        var type = await _db.RequestTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound(ApiResponse.Fail("Request type not found"));
        if (body.WorkflowDefinitionId is { } wid && !await _db.WorkflowDefinitions.AnyAsync(w => w.Id == wid, ct))
            return BadRequest(ApiResponse.Fail("Workflow not found"));
        type.WorkflowDefinitionId = body.WorkflowDefinitionId;
        await _db.SaveChangesAsync(ct);
        return OkResponse("Workflow assigned");
    }

    /// <summary>Activate / deactivate a request type. Activation requires a form and an assigned workflow.</summary>
    [HttpPut("types/{id:guid}/active")]
    [RequirePermission("Platform.Workflows.Edit")]
    public async Task<ActionResult<ApiResponse>> SetActive(Guid id, [FromBody] SetActiveBody body, CancellationToken ct)
    {
        var type = await _db.RequestTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound(ApiResponse.Fail("Request type not found"));
        if (body.IsActive)
        {
            if (type.FormDefinitionId == Guid.Empty)
                return BadRequest(ApiResponse.Fail("لا يمكن التفعيل: لا يوجد نموذج مرتبط بنوع الطلب"));
            if (type.WorkflowDefinitionId is null)
                return BadRequest(ApiResponse.Fail("لا يمكن التفعيل: يجب تعيين مسار موافقة أولاً"));
        }
        type.IsActive = body.IsActive;
        await _db.SaveChangesAsync(ct);
        return OkResponse(body.IsActive ? "Activated" : "Deactivated");
    }

    /// <summary>Link (or clear) the official document template a request type prints.</summary>
    [HttpPut("types/{id:guid}/print-template")]
    [RequirePermission("Platform.Documents.Edit")]
    public async Task<ActionResult<ApiResponse>> SetPrintTemplate(Guid id, [FromBody] SetPrintTemplateBody body, CancellationToken ct)
    {
        var type = await _db.RequestTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound(ApiResponse.Fail("Request type not found"));
        if (body.TemplateId is { } tid && !await _db.DocumentTemplates.AnyAsync(d => d.Id == tid, ct))
            return BadRequest(ApiResponse.Fail("Template not found"));

        type.PrintTemplateId = body.TemplateId;
        var mapping = await _db.RequestImpactMappings.FirstOrDefaultAsync(m => m.RequestTypeId == id, ct);
        if (mapping is null) { mapping = new Domain.Engines.Requests.RequestImpactMapping { RequestTypeId = id }; _db.RequestImpactMappings.Add(mapping); }
        mapping.GeneratesDocument = body.TemplateId is not null;
        await _db.SaveChangesAsync(ct);
        return OkResponse("Template linked");
    }

    // ── Admin: request → template mappings (multiple templates per type, each with a trigger) ──

    /// <summary>All document-template mappings for a request type (template + trigger event).</summary>
    [HttpGet("types/{id:guid}/templates")]
    [RequirePermission("Platform.Documents.View")]
    public async Task<ActionResult<ApiResponse<List<RequestTemplateMappingDto>>>> GetTemplateMappings(Guid id, CancellationToken ct)
    {
        var rows = await (from m in _db.RequestTemplateMappings
                          where m.RequestTypeId == id
                          join d in _db.DocumentTemplates on m.DocumentTemplateId equals d.Id into dj
                          from d in dj.DefaultIfEmpty()
                          orderby m.TriggerEvent, m.SortOrder
                          select new RequestTemplateMappingDto
                          {
                              Id = m.Id, RequestTypeId = m.RequestTypeId, DocumentTemplateId = m.DocumentTemplateId,
                              TemplateNameAr = d != null ? d.NameAr : null, TriggerEvent = m.TriggerEvent.ToString(),
                              IsSystem = m.IsSystem, IsActive = m.IsActive,
                          }).ToListAsync(ct);
        return OkResponse(rows);
    }

    /// <summary>Assign a template to a request type for a trigger event.</summary>
    [HttpPost("types/{id:guid}/templates")]
    [RequirePermission("Platform.Documents.Edit")]
    public async Task<ActionResult<ApiResponse<RequestTemplateMappingDto>>> AddTemplateMapping(Guid id, [FromBody] AddTemplateMappingBody body, CancellationToken ct)
    {
        if (!await _db.RequestTypes.AnyAsync(t => t.Id == id, ct)) return NotFound(ApiResponse.Fail("Request type not found"));
        if (!await _db.DocumentTemplates.AnyAsync(d => d.Id == body.TemplateId, ct)) return BadRequest(ApiResponse.Fail("Template not found"));
        if (!Enum.TryParse<DocumentTriggerEvent>(body.TriggerEvent, true, out var trig)) trig = DocumentTriggerEvent.FinalApproval;
        if (await _db.RequestTemplateMappings.AnyAsync(m => m.RequestTypeId == id && m.DocumentTemplateId == body.TemplateId && m.TriggerEvent == trig, ct))
            return BadRequest(ApiResponse.Fail("هذا القالب مرتبط بالفعل بنفس الحدث"));

        var mapping = new Domain.Engines.Requests.RequestTemplateMapping { RequestTypeId = id, DocumentTemplateId = body.TemplateId, TriggerEvent = trig, IsSystem = false };
        _db.RequestTemplateMappings.Add(mapping);
        await _db.SaveChangesAsync(ct);
        var name = await _db.DocumentTemplates.Where(d => d.Id == body.TemplateId).Select(d => d.NameAr).FirstOrDefaultAsync(ct);
        return CreatedResponse(new RequestTemplateMappingDto
        {
            Id = mapping.Id, RequestTypeId = id, DocumentTemplateId = body.TemplateId, TemplateNameAr = name,
            TriggerEvent = trig.ToString(), IsSystem = false, IsActive = true,
        });
    }

    /// <summary>Remove a (non-system) request → template mapping.</summary>
    [HttpDelete("template-mappings/{mappingId:guid}")]
    [RequirePermission("Platform.Documents.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteTemplateMapping(Guid mappingId, CancellationToken ct)
    {
        var m = await _db.RequestTemplateMappings.FirstOrDefaultAsync(x => x.Id == mappingId, ct);
        if (m is null) return NotFound(ApiResponse.Fail("Mapping not found"));
        if (m.IsSystem) return BadRequest(ApiResponse.Fail("لا يمكن حذف ربط نظام افتراضي"));
        _db.RequestTemplateMappings.Remove(m);
        await _db.SaveChangesAsync(ct);
        return OkResponse("Mapping removed");
    }

    // ── Submit / decide / cancel ────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RequestInstanceDto>>> Submit([FromBody] SubmitRequestBody body, CancellationToken ct)
    {
        var instance = await _engine.SubmitAsync(body.RequestTypeId, body.Values ?? new(), ct);
        return CreatedResponse(await MapInstanceAsync(instance.Id, ct));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<ApiResponse<RequestInstanceDto>>> Approve(Guid id, [FromBody] DecisionBody? body, CancellationToken ct)
    {
        await _engine.DecideAsync(id, true, body?.Comment, ct);
        return OkResponse(await MapInstanceAsync(id, ct));
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<RequestInstanceDto>>> Reject(Guid id, [FromBody] DecisionBody? body, CancellationToken ct)
    {
        await _engine.DecideAsync(id, false, body?.Comment, ct);
        return OkResponse(await MapInstanceAsync(id, ct));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<RequestInstanceDto>>> Cancel(Guid id, CancellationToken ct)
    {
        await _engine.CancelAsync(id, ct);
        return OkResponse(await MapInstanceAsync(id, ct));
    }

    // ── My requests / approvals inbox / detail ──────────────────────────────────

    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<List<RequestInstanceDto>>>> Mine(CancellationToken ct)
    {
        var empId = await _db.Employees.Where(e => e.UserId == _user.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (empId is null) return OkResponse(new List<RequestInstanceDto>());
        var list = await _db.RequestInstances.Where(r => r.EmployeeId == empId)
            .OrderByDescending(r => r.SubmittedAt).Select(ProjectInstance).ToListAsync(ct);
        return OkResponse(list);
    }

    /// <summary>All requests submitted by a specific employee (for the employee profile).</summary>
    [HttpGet("by-employee/{employeeId:guid}")]
    public async Task<ActionResult<ApiResponse<List<RequestInstanceDto>>>> ByEmployee(Guid employeeId, CancellationToken ct)
    {
        var list = await _db.RequestInstances.Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.SubmittedAt).Select(ProjectInstance).ToListAsync(ct);
        return OkResponse(list);
    }

    /// <summary>An employee's activity timeline (no extra permission — used by the profile).</summary>
    [HttpGet("by-employee/{employeeId:guid}/timeline")]
    public async Task<ActionResult<ApiResponse<List<EmployeeTimelineDto>>>> EmployeeTimeline(Guid employeeId, CancellationToken ct)
    {
        var events = await _db.TimelineEvents
            .Where(t => t.EntityType == "Employee" && t.EntityId == employeeId)
            .OrderByDescending(t => t.OccurredAt).Take(50)
            .Select(t => new EmployeeTimelineDto
            {
                Id = t.Id, Category = t.Category, Action = t.Action,
                DescriptionAr = t.DescriptionAr, DescriptionEn = t.DescriptionEn,
                ActorName = t.ActorName, OccurredAt = t.OccurredAt,
            }).ToListAsync(ct);
        return OkResponse(events);
    }

    /// <summary>Approved leave records (the Leaves page). scope=all (with permission) → everyone.</summary>
    [HttpGet("leaves")]
    public async Task<ActionResult<ApiResponse<List<LeaveRecordDto>>>> Leaves([FromQuery] string? scope, CancellationToken ct)
    {
        var canSeeAll = _user.Permissions.Contains("Platform.MasterData.Edit") || _user.Permissions.Contains("Employees.View");
        var q = _db.RequestInstances.Where(r => r.LeaveTypeId != null && r.Status == RequestStatus.Approved);
        if (!(scope == "all" && canSeeAll))
        {
            var myId = await _db.Employees.Where(e => e.UserId == _user.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            q = q.Where(r => r.EmployeeId == myId);
        }
        var rows = await (from r in q
                          join emp in _db.Employees on r.EmployeeId equals emp.Id into ej
                          from emp in ej.DefaultIfEmpty()
                          join lt in _db.MasterDataItems on r.LeaveTypeId equals lt.Id into lj
                          from lt in lj.DefaultIfEmpty()
                          orderby r.SubmittedAt descending
                          select new LeaveRecordDto
                          {
                              Id = r.Id, RequestNumber = r.RequestNumber, EmployeeId = r.EmployeeId,
                              EmployeeName = emp != null ? ((emp.FirstNameAr ?? emp.FirstName) + " " + (emp.LastNameAr ?? emp.LastName)) : null,
                              LeaveType = lt != null ? lt.NameAr : null,
                              StartDate = r.StartDate, EndDate = r.EndDate, DaysCount = r.DaysCount,
                              GeneratedDocumentId = r.GeneratedDocumentId,
                          }).ToListAsync(ct);
        return OkResponse(rows);
    }

    [HttpGet("inbox")]
    public async Task<ActionResult<ApiResponse<List<RequestInstanceDto>>>> Inbox(CancellationToken ct)
    {
        var pendingInstanceIds = _db.RequestApprovals
            .Where(a => a.AssignedToUserId == _user.UserId && a.Status == RequestApprovalStatus.Pending)
            .Select(a => a.RequestInstanceId);
        var list = await _db.RequestInstances
            .Where(r => pendingInstanceIds.Contains(r.Id) && (r.Status == RequestStatus.Submitted || r.Status == RequestStatus.InProgress))
            .OrderByDescending(r => r.SubmittedAt).Select(ProjectInstance).ToListAsync(ct);
        return OkResponse(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<RequestInstanceDto>>> Detail(Guid id, CancellationToken ct)
    {
        var dto = await MapInstanceAsync(id, ct);
        return dto is null ? NotFound(ApiResponse.Fail("Request not found")) : OkResponse(dto);
    }

    /// <summary>
    /// Completion status for a request: the overall run plus each effect that ran (type, status,
    /// duration, target record, failure reason). Populated by the Completion Effects Engine after
    /// final approval. Returns null run if the request has not completed yet.
    /// </summary>
    [HttpGet("{id:guid}/completion")]
    public async Task<ActionResult<ApiResponse<CompletionRunDto?>>> Completion(Guid id, CancellationToken ct)
    {
        var run = await _db.CompletionRuns.Where(r => r.RequestInstanceId == id)
            .Select(r => new CompletionRunDto
            {
                Id = r.Id,
                RequestInstanceId = r.RequestInstanceId,
                Status = r.Status.ToString(),
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                DurationMs = r.DurationMs,
                Attempts = r.Attempts,
                FailureReason = r.FailureReason,
            }).FirstOrDefaultAsync(ct);

        if (run is not null)
            run.Effects = await _db.CompletionEffects.Where(e => e.CompletionRunId == run.Id).OrderBy(e => e.Sequence)
                .Select(e => new CompletionEffectDto
                {
                    EffectType = e.EffectType,
                    Sequence = e.Sequence,
                    Status = e.Status.ToString(),
                    DurationMs = e.DurationMs,
                    ExecutorName = e.ExecutorName,
                    ExecutorVersion = e.ExecutorVersion,
                    TargetEntityType = e.TargetEntityType,
                    TargetRecordId = e.TargetRecordId,
                    ResultSummary = e.ResultSummary,
                    FailureReason = e.FailureReason,
                    ExecutedAt = e.ExecutedAt,
                }).ToListAsync(ct);

        return OkResponse(run);
    }

    /// <summary>Download the official PDF for a request (logo, CR/VAT, details, approvals, QR).</summary>
    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var renderer = HttpContext.RequestServices.GetRequiredService<Services.Documents.IDocumentRenderer>();
        var (pdf, fileName) = await renderer.RenderRequestPdfAsync(id, ct);
        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>All generated documents for a request (one per fired template mapping).</summary>
    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<ApiResponse<List<GeneratedDocInfo>>>> Documents(Guid id, CancellationToken ct)
    {
        var docs = await (from gd in _db.GeneratedDocuments
                          where gd.EntityType == "RequestInstance" && gd.EntityId == id
                          join d in _db.DocumentTemplates on gd.DocumentTemplateId equals d.Id into dj
                          from d in dj.DefaultIfEmpty()
                          orderby gd.GeneratedAt descending
                          select new GeneratedDocInfo
                          {
                              Id = gd.Id, DocumentTemplateId = gd.DocumentTemplateId,
                              TemplateNameAr = d != null ? d.NameAr : null, FileName = gd.FileName, GeneratedAt = gd.GeneratedAt,
                          }).ToListAsync(ct);
        return OkResponse(docs);
    }

    /// <summary>View (inline) or download a specific generated document — rendered on demand.</summary>
    [HttpGet("{id:guid}/documents/{docId:guid}")]
    public async Task<IActionResult> ViewDocument(Guid id, Guid docId, [FromQuery] bool download, CancellationToken ct)
    {
        var gd = await _db.GeneratedDocuments.FirstOrDefaultAsync(g => g.Id == docId && g.EntityId == id, ct);
        if (gd is null) return NotFound();
        var renderer = HttpContext.RequestServices.GetRequiredService<Services.Documents.IDocumentRenderer>();
        var (pdf, fileName) = await renderer.RenderRequestPdfAsync(id, gd.DocumentTemplateId, ct);
        return download ? File(pdf, "application/pdf", fileName) : File(pdf, "application/pdf");
    }

    /// <summary>Notify the requester by email that a document is ready (uses the notification engine).</summary>
    [HttpPost("{id:guid}/documents/{docId:guid}/email")]
    public async Task<ActionResult<ApiResponse>> EmailDocument(Guid id, Guid docId, CancellationToken ct)
    {
        var instance = await _db.RequestInstances.Include(r => r.RequestType).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (instance is null) return NotFound(ApiResponse.Fail("Request not found"));
        if (!await _db.GeneratedDocuments.AnyAsync(g => g.Id == docId && g.EntityId == id, ct)) return NotFound(ApiResponse.Fail("Document not found"));
        var uid = await _db.Employees.Where(e => e.Id == instance.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        if (uid is null) return BadRequest(ApiResponse.Fail("لا يوجد مستخدم مرتبط لإرسال البريد"));

        var notify = HttpContext.RequestServices.GetRequiredService<Services.Notifications.INotificationService>();
        await notify.NotifyAsync(uid.Value, "مستند جاهز", "Document ready",
            $"مستند {instance.RequestType.NameAr} للطلب {instance.RequestNumber} جاهز للتحميل.",
            $"Your {instance.RequestType.NameEn} document for {instance.RequestNumber} is ready to download.",
            "Document", instance.Id, "/requests", email: true, ct: ct);
        await _db.SaveChangesAsync(ct);
        return OkResponse("Email queued");
    }

    // ── Projection helpers ──────────────────────────────────────────────────────

    private static readonly System.Linq.Expressions.Expression<Func<Domain.Engines.Requests.RequestInstance, RequestInstanceDto>> ProjectInstance =
        r => new RequestInstanceDto
        {
            Id = r.Id, RequestNumber = r.RequestNumber, RequestTypeId = r.RequestTypeId,
            RequestTypeNameAr = r.RequestType.NameAr, RequestTypeNameEn = r.RequestType.NameEn,
            Status = r.Status.ToString(), SubmittedAt = r.SubmittedAt, DecidedAt = r.DecidedAt,
            CurrentStepOrder = r.CurrentStepOrder,
            StartDate = r.StartDate, EndDate = r.EndDate, DaysCount = r.DaysCount,
            GeneratedDocumentId = r.GeneratedDocumentId,
        };

    private async Task<RequestInstanceDto?> MapInstanceAsync(Guid id, CancellationToken ct)
    {
        var dto = await _db.RequestInstances.Where(r => r.Id == id).Select(ProjectInstance).FirstOrDefaultAsync(ct);
        if (dto is null) return null;
        dto.Approvals = await _db.RequestApprovals.Where(a => a.RequestInstanceId == id).OrderBy(a => a.StepOrder)
            .Select(a => new RequestApprovalDto
            {
                StepOrder = a.StepOrder, StepNameAr = a.StepNameAr, StepNameEn = a.StepNameEn,
                Status = a.Status.ToString(), Comment = a.Comment, DecidedAt = a.DecidedAt,
            }).ToListAsync(ct);
        dto.History = await _db.RequestStatusHistories.Where(h => h.RequestInstanceId == id).OrderBy(h => h.At)
            .Select(h => new RequestHistoryDto { ToStatus = h.ToStatus.ToString(), NoteAr = h.NoteAr, NoteEn = h.NoteEn, At = h.At }).ToListAsync(ct);
        return dto;
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed class SubmitRequestBody
{
    public Guid RequestTypeId { get; set; }
    public List<RequestValueInput>? Values { get; set; }
}
public sealed class DecisionBody { public string? Comment { get; set; } }
public sealed class SetPrintTemplateBody { public Guid? TemplateId { get; set; } }
public sealed class AddTemplateMappingBody { public Guid TemplateId { get; set; } public string TriggerEvent { get; set; } = "FinalApproval"; }

public sealed class CompletionRunDto
{
    public Guid Id { get; set; }
    public Guid RequestInstanceId { get; set; }
    public string Status { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public int Attempts { get; set; }
    public string? FailureReason { get; set; }
    public List<CompletionEffectDto> Effects { get; set; } = new();
}

public sealed class CompletionEffectDto
{
    public string EffectType { get; set; } = null!;
    public int Sequence { get; set; }
    public string Status { get; set; } = null!;
    public int? DurationMs { get; set; }
    public string? ExecutorName { get; set; }
    public string? ExecutorVersion { get; set; }
    public string? TargetEntityType { get; set; }
    public Guid? TargetRecordId { get; set; }
    public string? ResultSummary { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? ExecutedAt { get; set; }
}

public sealed class RequestTemplateMappingDto
{
    public Guid Id { get; set; }
    public Guid RequestTypeId { get; set; }
    public Guid DocumentTemplateId { get; set; }
    public string? TemplateNameAr { get; set; }
    public string TriggerEvent { get; set; } = null!;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LeaveRecordDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? LeaveType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DaysCount { get; set; }
    public Guid? GeneratedDocumentId { get; set; }
}

public sealed class GeneratedDocInfo
{
    public Guid Id { get; set; }
    public Guid DocumentTemplateId { get; set; }
    public string? TemplateNameAr { get; set; }
    public string? FileName { get; set; }
    public DateTime? GeneratedAt { get; set; }
}

public sealed class RequestTypeAdminDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public Guid FormDefinitionId { get; set; }
    public Guid? WorkflowDefinitionId { get; set; }
    public string? WorkflowName { get; set; }
    public Guid? PrintTemplateId { get; set; }
    public string? PrintTemplateName { get; set; }
    public bool ActivationReady { get; set; }
}

public sealed class SetWorkflowBody { public Guid? WorkflowDefinitionId { get; set; } }
public sealed class SetActiveBody { public bool IsActive { get; set; } }

public sealed class RequestTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string Kind { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsSystem { get; set; }
    public bool HasWorkflow { get; set; }
    public bool GeneratesDocument { get; set; }
}

public sealed class RequestTypeDetailDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string Kind { get; set; } = null!;
    public Guid FormDefinitionId { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsLeaveRequest { get; set; }
    public List<RequestFieldDto> Fields { get; set; } = new();
}

public sealed class LeavePreviewBody
{
    public Guid LeaveTypeId { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public bool HasAttachment { get; set; }
}

public sealed class SetLeaveBalanceBody
{
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public decimal EntitledDays { get; set; }
    public decimal CarriedForwardDays { get; set; }
}

public sealed class RequestFieldDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string FieldType { get; set; } = null!;
    public bool IsRequired { get; set; }
    public string? Placeholder { get; set; }
    public string? Options { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RequestInstanceDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = null!;
    public Guid RequestTypeId { get; set; }
    public string RequestTypeNameAr { get; set; } = null!;
    public string RequestTypeNameEn { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public int CurrentStepOrder { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DaysCount { get; set; }
    public Guid? GeneratedDocumentId { get; set; }
    public List<RequestApprovalDto> Approvals { get; set; } = new();
    public List<RequestHistoryDto> History { get; set; } = new();
}

public sealed class RequestApprovalDto
{
    public int StepOrder { get; set; }
    public string StepNameAr { get; set; } = null!;
    public string StepNameEn { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Comment { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public sealed class RequestHistoryDto
{
    public string ToStatus { get; set; } = null!;
    public string? NoteAr { get; set; }
    public string? NoteEn { get; set; }
    public DateTime At { get; set; }
}

public sealed class EmployeeTimelineDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? ActorName { get; set; }
    public DateTime OccurredAt { get; set; }
}
