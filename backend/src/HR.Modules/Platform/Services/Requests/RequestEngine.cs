using System.Text.Json;
using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Audit;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Workflows;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Requests;

public sealed class RequestEngine : IRequestEngine
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ITimelineEngine _timeline;
    private readonly IAuditEngine _audit;
    private readonly ILeaveService _leave;
    private readonly ICompletionEngine _completion;
    private readonly HR.Modules.Platform.Services.Documents.IDocumentGenerationService _docGen;
    private readonly HR.Modules.Platform.Services.Notifications.IWorkflowNotificationDispatcher _dispatcher;

    private const int SlaHours = 48;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public RequestEngine(ApplicationDbContext db, ICurrentUserService user, ITimelineEngine timeline, IAuditEngine audit, ILeaveService leave,
        ICompletionEngine completion,
        HR.Modules.Platform.Services.Documents.IDocumentGenerationService docGen,
        HR.Modules.Platform.Services.Notifications.IWorkflowNotificationDispatcher dispatcher)
    {
        _db = db; _user = user; _timeline = timeline; _audit = audit; _leave = leave; _completion = completion; _docGen = docGen; _dispatcher = dispatcher;
    }

    // ── Submit ────────────────────────────────────────────────────────────────

    public async Task<RequestInstance> SubmitAsync(Guid requestTypeId, IReadOnlyList<RequestValueInput> values, CancellationToken ct)
    {
        var type = await _db.RequestTypes.Include(t => t.ImpactMapping)
            .FirstOrDefaultAsync(t => t.Id == requestTypeId && t.IsActive, ct)
            ?? throw new NotFoundException("RequestType", requestTypeId);

        if (type.FormDefinitionId == Guid.Empty)
            throw Invalid("form", "This request type has no form configured.");

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == _user.UserId, ct)
            ?? throw Invalid("employee", "No employee profile is linked to your account.");

        // Resolve field ids from codes (the FK is enforced; clients send codes).
        var fields = await _db.FormFields.Where(f => f.FormDefinitionId == type.FormDefinitionId).ToListAsync(ct);
        var fieldByCode = fields.ToDictionary(f => f.Code, f => f.Id, StringComparer.OrdinalIgnoreCase);

        // Validate required fields are present
        var provided = new HashSet<string>(values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => v.FieldCode), StringComparer.OrdinalIgnoreCase);
        var missing = fields.Where(f => f.IsRequired && !provided.Contains(f.Code)).Select(f => f.NameEn).ToList();
        if (missing.Count > 0)
            throw Invalid("fields", $"Missing required field(s): {string.Join(", ", missing)}");

        // 1) Persist the form submission
        var submission = new FormSubmission
        {
            FormDefinitionId = type.FormDefinitionId,
            SubmittedById = _user.UserId,
            SubmittedAt = DateTime.UtcNow,
            Status = FormSubmissionStatus.Submitted,
        };
        foreach (var v in values)
        {
            var fieldId = v.FormFieldId ?? (fieldByCode.TryGetValue(v.FieldCode, out var fid) ? fid : (Guid?)null);
            if (fieldId is null) continue; // ignore values for unknown fields (never break the FK)
            submission.Values.Add(new FormSubmissionValue { FormFieldId = fieldId.Value, FieldCode = v.FieldCode, Value = v.Value, FileUrl = v.FileUrl });
        }
        _db.FormSubmissions.Add(submission);

        // 2) Create the request instance (+ leave snapshot, object-driven)
        var instance = new RequestInstance
        {
            RequestTypeId = type.Id,
            RequestNumber = await NextRequestNumberAsync(ct),
            EmployeeId = employee.Id,
            FormSubmissionId = submission.Id,
            Status = RequestStatus.Submitted,
            SubmittedAt = DateTime.UtcNow,
        };

        // Leave requests are generic: the sub-type is the selected LeaveType (object ref),
        // and that type's settings (rules) drive validation + impacts.
        var isLeave = type.ImpactMapping?.AffectsLeaveBalance == true;
        if (isLeave)
        {
            var leaveTypeId = ParseGuid(Val(values, RequestFieldCodes.LeaveType))
                ?? throw Invalid("leaveType", "يرجى اختيار نوع الإجازة");
            instance.LeaveTypeId = leaveTypeId;
            instance.StartDate = ParseDate(Val(values, RequestFieldCodes.StartDate));
            instance.EndDate = ParseDate(Val(values, RequestFieldCodes.EndDate));

            var leaveItem = await _db.MasterDataItems.FirstOrDefaultAsync(m => m.Id == leaveTypeId, ct)
                ?? throw Invalid("leaveType", "نوع الإجازة غير موجود");
            var rules = _leave.GetRules(leaveItem.MetadataJson);
            if (instance.StartDate is { } sd && instance.EndDate is { } ed)
                instance.DaysCount = _leave.ComputeDays(sd, ed, rules);

            var hasAttachment = values.Any(v => v.FieldCode.Equals(RequestFieldCodes.Attachment, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v.FileUrl));
            var preview = await _leave.PreviewAsync(employee.Id, leaveTypeId, instance.StartDate, instance.EndDate, hasAttachment, ct);
            if (!preview.IsValid)
                throw Invalid("leave", string.Join(" • ", preview.Errors));
        }
        _db.RequestInstances.Add(instance);

        // 3) Resolve approval chain from the linked workflow + create a workflow instance
        var chain = await BuildApprovalChainAsync(type, employee, instance, values, ct);
        if (chain.Count > 0)
        {
            var (wfInstanceId, _) = await StartWorkflowAsync(type, instance.Id, ct);
            instance.WorkflowInstanceId = wfInstanceId;
            for (int i = 0; i < chain.Count; i++)
            {
                chain[i].RequestInstanceId = instance.Id;
                chain[i].StepOrder = i + 1;
                chain[i].Status = RequestApprovalStatus.Pending;
                chain[i].DueAt = DateTime.UtcNow.AddHours(SlaHours);   // SLA per step
                _db.RequestApprovals.Add(chain[i]);
            }
            instance.Status = RequestStatus.InProgress;
            instance.CurrentStepOrder = 1;
        }

        AddHistory(instance, null, instance.Status, "تم تقديم الطلب", "Request submitted");
        await _db.SaveChangesAsync(ct);

        // 4) Side records: timeline, audit, notifications
        await _timeline.PublishEvent("Requests", "RequestInstance", instance.Id, "Submitted",
            $"Request {instance.RequestNumber} submitted", $"تم تقديم الطلب {instance.RequestNumber}", new { type.Code }, ct);
        await _audit.LogChange("RequestInstance", instance.Id, "Submitted", null, new { instance.RequestNumber, type.Code }, ct);

        await _dispatcher.DispatchAsync(WorkflowNotificationEvent.Submitted, instance, null, ct);
        if (chain.Count > 0)
        {
            var firstStep = chain.OrderBy(a => a.StepOrder).FirstOrDefault();
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.StepAssigned, instance, firstStep, ct);
        }
        else
        {
            // No approval chain → the request is final on submit: run completion immediately.
            var completion = await _completion.ExecuteAsync(instance.Id, ct);
            instance.Status = completion.Success ? RequestStatus.Approved : RequestStatus.CompletionFailed;
            instance.DecidedAt = DateTime.UtcNow;
            if (completion.Success)
                await _dispatcher.DispatchAsync(WorkflowNotificationEvent.FinalApproved, instance, null, ct);
        }

        // Trigger: Submitted (always). If there is no approval chain the request is final on submit.
        await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.Submitted, ct);
        if (chain.Count == 0)
        {
            await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.FinalApproval, ct);
            await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.Completed, ct);
        }

        await _db.SaveChangesAsync(ct);
        return instance;
    }

    // ── Decide (approve / reject) ───────────────────────────────────────────────

    public async Task<RequestInstance> DecideAsync(Guid requestInstanceId, bool approved, string? comment, CancellationToken ct)
    {
        var instance = await _db.RequestInstances
            .Include(r => r.Approvals)
            .Include(r => r.RequestType).ThenInclude(t => t.ImpactMapping)
            .FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new NotFoundException("RequestInstance", requestInstanceId);

        if (instance.Status is not (RequestStatus.Submitted or RequestStatus.InProgress))
            throw Invalid("status", "This request is not awaiting a decision.");

        var step = instance.Approvals
            .Where(a => a.Status == RequestApprovalStatus.Pending)
            .OrderBy(a => a.StepOrder).FirstOrDefault()
            ?? throw Invalid("step", "No pending approval step.");

        var isAdmin = await IsAdminAsync(ct);
        if (step.AssignedToUserId != _user.UserId && !isAdmin)
            throw new ForbiddenException("This approval is not assigned to you.");

        step.DecidedByUserId = _user.UserId;
        step.DecidedAt = DateTime.UtcNow;
        step.Comment = comment;

        if (!approved)
        {
            step.Status = RequestApprovalStatus.Rejected;
            await TransitionAsync(instance, RequestStatus.Rejected, comment, "تم رفض الطلب", "Request rejected", ct);
            await CloseWorkflowAsync(instance, WorkflowStatus.Rejected, ct);
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.Rejected, instance, step, ct);
            await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.Rejected, ct);
            await _db.SaveChangesAsync(ct);
            return instance;
        }

        step.Status = RequestApprovalStatus.Approved;

        // Trigger: FirstApproval — fired the first time any approver approves (step 1).
        if (step.StepOrder == 1)
            await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.FirstApproval, ct);

        var next = instance.Approvals
            .Where(a => a.Status == RequestApprovalStatus.Pending)
            .OrderBy(a => a.StepOrder).FirstOrDefault();

        if (next is not null)
        {
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.StepApproved, instance, step, ct);
            instance.CurrentStepOrder = next.StepOrder;
            await TransitionAsync(instance, RequestStatus.InProgress, comment, "تمت موافقة خطوة", "Step approved", ct);
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.StepAssigned, instance, next, ct);
            await _db.SaveChangesAsync(ct);
            return instance;
        }

        // Final approval → hand off to the Completion Effects Engine. The engine resolves the
        // request's effects, runs them in ONE transaction, and tracks/audits each. The Request
        // engine no longer touches any business module directly.
        await TransitionAsync(instance, RequestStatus.Approved, comment, "تمت الموافقة على الطلب", "Request approved", ct);
        await CloseWorkflowAsync(instance, WorkflowStatus.Completed, ct);
        var completion = await _completion.ExecuteAsync(instance.Id, ct);
        if (completion.Success)
        {
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.FinalApproved, instance, null, ct);
            // Triggers: FinalApproval + Completed (last approver approved → request is final).
            await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.FinalApproval, ct);
            await _docGen.GenerateForTriggerAsync(instance.Id, DocumentTriggerEvent.Completed, ct);
        }
        else
        {
            // Workflow is approved but the effects were rolled back — flag for support (already notified).
            instance.Status = RequestStatus.CompletionFailed;
        }
        await _db.SaveChangesAsync(ct);
        return instance;
    }

    public async Task<RequestInstance> ReturnAsync(Guid requestInstanceId, string? comment, CancellationToken ct)
    {
        var instance = await _db.RequestInstances.Include(r => r.Approvals).Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new NotFoundException("RequestInstance", requestInstanceId);
        if (instance.Status is not (RequestStatus.Submitted or RequestStatus.InProgress))
            throw Invalid("status", "This request is not awaiting a decision.");

        var step = instance.Approvals.Where(a => a.Status == RequestApprovalStatus.Pending)
            .OrderBy(a => a.StepOrder).FirstOrDefault()
            ?? throw Invalid("step", "No pending approval step.");

        var isAdmin = await IsAdminAsync(ct);
        if (step.AssignedToUserId != _user.UserId && !isAdmin)
            throw new ForbiddenException("This approval is not assigned to you.");

        step.Status = RequestApprovalStatus.Returned;
        step.DecidedByUserId = _user.UserId;
        step.DecidedAt = DateTime.UtcNow;
        step.Comment = comment;
        await TransitionAsync(instance, RequestStatus.Returned, comment, "أُعيد الطلب للتعديل", "Returned for changes", ct);
        await CloseWorkflowAsync(instance, WorkflowStatus.Cancelled, ct);
        await _dispatcher.DispatchAsync(WorkflowNotificationEvent.Returned, instance, step, ct);
        await _db.SaveChangesAsync(ct);
        return instance;
    }

    public async Task<RequestInstance> CancelAsync(Guid requestInstanceId, CancellationToken ct)
    {
        var instance = await _db.RequestInstances.FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new NotFoundException("RequestInstance", requestInstanceId);
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == _user.UserId, ct);
        if (employee is null || instance.EmployeeId != employee.Id)
            throw new ForbiddenException("You can only cancel your own requests.");
        if (instance.Status is RequestStatus.Approved or RequestStatus.Rejected or RequestStatus.Cancelled or RequestStatus.CompletionFailed)
            throw Invalid("status", "This request can no longer be cancelled.");

        await TransitionAsync(instance, RequestStatus.Cancelled, null, "تم إلغاء الطلب", "Request cancelled", ct);
        await CloseWorkflowAsync(instance, WorkflowStatus.Cancelled, ct);
        await _db.SaveChangesAsync(ct);
        return instance;
    }

    // ── Approval-chain resolution from the linked workflow ──────────────────────

    private async Task<List<RequestApproval>> BuildApprovalChainAsync(RequestType type, HR.Modules.Employees.Entities.Employee employee,
        RequestInstance instance, IReadOnlyList<RequestValueInput> values, CancellationToken ct)
    {
        var result = new List<RequestApproval>();
        if (type.WorkflowDefinitionId is not { } wfId) return result;

        var version = await _db.WorkflowVersions
            .Where(v => v.WorkflowDefinitionId == wfId && v.IsPublished)
            .OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync(ct);
        if (version?.Configuration is null) return result;

        WorkflowChainConfig? cfg;
        try { cfg = JsonSerializer.Deserialize<WorkflowChainConfig>(version.Configuration, Json); }
        catch { cfg = null; }
        if (cfg?.Steps is null) return result;

        // Condition context: a step only joins the chain when ALL its conditions hold. This lets a
        // business user add rules like "Leave Days > 5 → require HR" without any code.
        var conditionCtx = BuildConditionContext(employee, instance, values);

        foreach (var s in cfg.Steps)
        {
            if (!RequestConditions.Met(s.Conditions, conditionCtx)) continue;

            var approverType = (ApproverType)s.ApproverType;
            var assignee = await ResolveApproverAsync(s, employee, ct);
            result.Add(new RequestApproval
            {
                StepNameAr = s.NameAr,
                StepNameEn = s.NameEn,
                ApproverType = approverType,
                AssignedToUserId = assignee,
                Status = RequestApprovalStatus.Pending,
                CanReject = s.CanReject,
                CanReturn = s.CanReturn,
                CanDelegate = s.CanDelegate,
                IsOptional = !s.Required,
            });
        }
        return result;
    }

    // ── No-code condition evaluation ────────────────────────────────────────────

    private static Dictionary<string, string?> BuildConditionContext(
        HR.Modules.Employees.Entities.Employee employee, RequestInstance instance, IReadOnlyList<RequestValueInput> values)
    {
        var ctx = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["leaveDays"] = instance.DaysCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Val(values, RequestFieldCodes.Days),
            ["amount"] = Val(values, RequestFieldCodes.Amount),
            ["leaveType"] = instance.LeaveTypeId?.ToString() ?? Val(values, RequestFieldCodes.LeaveType),
            ["department"] = employee.DepartmentId?.ToString(),
            ["branch"] = employee.BranchId?.ToString(),
            ["employmentType"] = employee.EmploymentTypeId?.ToString(),
            ["jobTitle"] = employee.JobTitleId?.ToString(),
        };
        // Also expose every submitted form field by its code so conditions can reference any field.
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v.FieldCode)) ctx[v.FieldCode] = v.Value;
        return ctx;
    }

    private async Task<Guid?> ResolveApproverAsync(WorkflowStepConfig s, HR.Modules.Employees.Entities.Employee employee, CancellationToken ct)
    {
        var type = (ApproverType)s.ApproverType;
        Guid? resolved = type switch
        {
            // SpecificUser carries either a pre-resolved user id (legacy seeds) or an employee id picked in the builder.
            ApproverType.SpecificUser => s.SpecificUserId ?? await ManagerUserAsync(s.SpecificEntityId, ct),
            ApproverType.DirectManager => await ManagerUserAsync(employee.ManagerId, ct),
            ApproverType.DepartmentHead => await DepartmentHeadUserAsync(employee.DepartmentId, ct),
            ApproverType.HrManager => await UserByRoleKeywordAsync("HR", ct),
            ApproverType.Role => await UserByRoleIdAsync(s.SpecificEntityId, ct),
            ApproverType.ManagerChain => await ManagerChainUserAsync(employee, Math.Max(1, s.ChainLevel), ct),
            _ => null,
        };
        // Guarantee progress: fall back to a tenant admin so a chain never dead-ends.
        return resolved ?? await AdminUserIdAsync(ct);
    }

    private async Task<Guid?> ManagerUserAsync(Guid? managerEmployeeId, CancellationToken ct)
    {
        if (managerEmployeeId is not { } id) return null;
        return await _db.Employees.Where(e => e.Id == id).Select(e => e.UserId).FirstOrDefaultAsync(ct);
    }

    private async Task<Guid?> DepartmentHeadUserAsync(Guid? departmentId, CancellationToken ct)
    {
        if (departmentId is not { } id) return null;
        var managerEmpId = await _db.Departments.Where(d => d.Id == id).Select(d => d.ManagerId).FirstOrDefaultAsync(ct);
        return await ManagerUserAsync(managerEmpId, ct);
    }

    private async Task<Guid?> UserByRoleKeywordAsync(string keyword, CancellationToken ct)
    {
        var tid = _user.TenantId;
        return await (from u in _db.Users.Where(u => u.TenantId == tid && u.IsActive)
                      join ur in _db.UserRoles on u.Id equals ur.UserId
                      join r in _db.Roles on ur.RoleId equals r.Id
                      where EF.Functions.ILike(r.Name, $"%{keyword}%")
                      select (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>First active member of a specific role (Finance / CEO / any user group picked in the builder).</summary>
    private async Task<Guid?> UserByRoleIdAsync(Guid? roleId, CancellationToken ct)
    {
        if (roleId is not { } rid) return null;
        var tid = _user.TenantId;
        return await (from u in _db.Users.Where(u => u.TenantId == tid && u.IsActive)
                      join ur in _db.UserRoles on u.Id equals ur.UserId
                      where ur.RoleId == rid
                      select (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>Walk up the manager chain `levels` times (1 = direct manager) and return that user.</summary>
    private async Task<Guid?> ManagerChainUserAsync(HR.Modules.Employees.Entities.Employee employee, int levels, CancellationToken ct)
    {
        var currentEmpId = employee.ManagerId;
        for (int i = 1; i < levels && currentEmpId is { } id; i++)
            currentEmpId = await _db.Employees.Where(e => e.Id == id).Select(e => e.ManagerId).FirstOrDefaultAsync(ct);
        return await ManagerUserAsync(currentEmpId, ct);
    }

    private async Task<Guid?> AdminUserIdAsync(CancellationToken ct)
    {
        var tid = _user.TenantId;
        var admin = await (from u in _db.Users.Where(u => u.TenantId == tid && u.IsActive)
                           join ur in _db.UserRoles on u.Id equals ur.UserId
                           join r in _db.Roles on ur.RoleId equals r.Id
                           where r.IsSystemRole || EF.Functions.ILike(r.Name, "%admin%")
                           select (Guid?)u.Id).FirstOrDefaultAsync(ct);
        return admin ?? await _db.Users.Where(u => u.TenantId == tid && u.IsActive).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<bool> IsAdminAsync(CancellationToken ct)
    {
        var tid = _user.TenantId;
        return await (from ur in _db.UserRoles.Where(x => x.UserId == _user.UserId)
                      join r in _db.Roles on ur.RoleId equals r.Id
                      where r.TenantId == tid && (r.IsSystemRole || EF.Functions.ILike(r.Name, "%admin%"))
                      select r.Id).AnyAsync(ct);
    }

    // ── Workflow instance (traceability; chain is driven by RequestApproval) ────

    private async Task<(Guid instanceId, Guid versionId)> StartWorkflowAsync(RequestType type, Guid requestInstanceId, CancellationToken ct)
    {
        var versionId = await _db.WorkflowVersions
            .Where(v => v.WorkflowDefinitionId == type.WorkflowDefinitionId && v.IsPublished)
            .OrderByDescending(v => v.VersionNumber).Select(v => v.Id).FirstOrDefaultAsync(ct);

        var wf = new WorkflowInstance
        {
            WorkflowDefinitionId = type.WorkflowDefinitionId!.Value,
            WorkflowVersionId = versionId,
            EntityType = "RequestInstance",
            EntityId = requestInstanceId,
            Status = WorkflowStatus.Active,
            StartedAt = DateTime.UtcNow,
        };
        _db.WorkflowInstances.Add(wf);
        return (wf.Id, versionId);
    }

    private async Task CloseWorkflowAsync(RequestInstance instance, WorkflowStatus status, CancellationToken ct)
    {
        if (instance.WorkflowInstanceId is not { } id) return;
        var wf = await _db.WorkflowInstances.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wf is null) return;
        wf.Status = status;
        wf.CompletedAt = DateTime.UtcNow;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task TransitionAsync(RequestInstance instance, RequestStatus to, string? note, string noteAr, string noteEn, CancellationToken ct)
    {
        var from = instance.Status;
        instance.Status = to;
        if (to is RequestStatus.Approved or RequestStatus.Rejected or RequestStatus.Cancelled)
            instance.DecidedAt = DateTime.UtcNow;
        instance.DecisionNote = note ?? instance.DecisionNote;
        AddHistory(instance, from, to, noteAr, noteEn);
        await _timeline.PublishEvent("Requests", "RequestInstance", instance.Id, to.ToString(), noteEn, noteAr, null, ct);
        await _audit.LogChange("RequestInstance", instance.Id, to.ToString(), new { from }, new { to }, ct);
    }

    private void AddHistory(RequestInstance instance, RequestStatus? from, RequestStatus to, string noteAr, string noteEn)
        => _db.RequestStatusHistories.Add(new RequestStatusHistory
        {
            RequestInstanceId = instance.Id, FromStatus = from, ToStatus = to,
            ActorUserId = _user.UserId, NoteAr = noteAr, NoteEn = noteEn, At = DateTime.UtcNow,
        });

    private async Task<string> NextRequestNumberAsync(CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var count = await _db.RequestInstances.CountAsync(ct);
        return $"REQ-{year}-{(count + 1):D6}";
    }

    private static string? Val(IReadOnlyList<RequestValueInput> values, string code)
        => values.FirstOrDefault(v => string.Equals(v.FieldCode, code, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTime? ParseDate(string? raw)
        => DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

    private static Guid? ParseGuid(string? raw)
        => Guid.TryParse(raw, out var g) ? g : null;

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
