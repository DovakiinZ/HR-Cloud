using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Models;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Payroll.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Payroll.Controllers;

/// <summary>The Payroll application surface over the Financial Calculation Engine: provision a standard
/// payroll, preview, and drive a run through its lifecycle. Every endpoint is permission-gated; the engines
/// enforce the state machine, snapshots, validation and ledger posting.</summary>
[Authorize]
[ApiController]
[Route("api/payroll")]
public class PayrollController : BaseApiController
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollRunEngine _runEngine;
    private readonly IPayrollPreviewEngine _previewEngine;
    private readonly IPayrollExecutionScheduler _scheduler;
    private readonly IStandardPayrollSeeder _seeder;
    private readonly IPayrollTypeService _types;
    private readonly IScopeEngine _scope;
    private readonly IPayrollTransactionService _transactions;
    private readonly IPayrollTransactionReversalService _reversals;
    private readonly IAttendancePayrollSyncService _attendanceSync;
    private readonly IPayrollRunReadService _runRead;

    public PayrollController(ApplicationDbContext db, IPayrollRunEngine runEngine, IPayrollPreviewEngine previewEngine,
        IPayrollExecutionScheduler scheduler, IStandardPayrollSeeder seeder,
        IPayrollTypeService types, IScopeEngine scope, IPayrollTransactionService transactions,
        IPayrollTransactionReversalService reversals, IAttendancePayrollSyncService attendanceSync,
        IPayrollRunReadService runRead)
    {
        _db = db;
        _runEngine = runEngine;
        _previewEngine = previewEngine;
        _scheduler = scheduler;
        _seeder = seeder;
        _types = types;
        _scope = scope;
        _transactions = transactions;
        _reversals = reversals;
        _attendanceSync = attendanceSync;
        _runRead = runRead;
    }

    [HttpPost("bootstrap")]
    [RequirePermission("Payroll.Run")]
    public async Task<ActionResult<ApiResponse<Guid>>> Bootstrap(CancellationToken ct)
        => OkResponse(await _seeder.EnsureStandardMonthlyAsync(ct), "Standard monthly payroll is ready.");

    [HttpGet("definitions")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<List<PayrollDefinitionDto>>>> Definitions(CancellationToken ct)
    {
        var defs = await _db.PayrollDefinitions.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new PayrollDefinitionDto
            {
                Id = d.Id, Code = d.Code, Name = d.Name, NameAr = d.NameAr,
                Status = d.Status.ToString(), CurrentVersionId = d.CurrentVersionId,
            })
            .ToListAsync(ct);
        return OkResponse(defs);
    }

    [HttpPost("preview")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<PayrollPreviewDto>>> Preview([FromBody] PreviewRequest req, CancellationToken ct)
    {
        var versionId = await ResolveVersionAsync(req.DefinitionId, ct);
        var preview = await _previewEngine.PreviewAsync(versionId, PayrollPeriod.Monthly(req.Year, req.Month), ct);
        return OkResponse(new PayrollPreviewDto
        {
            EmployeeCount = preview.EmployeeCount,
            GrossTotal = preview.GrossTotal,
            DeductionTotal = preview.DeductionTotal,
            NetTotal = preview.NetTotal,
            Currency = preview.Currency,
            IsValid = preview.Validation.IsValid,
            Findings = preview.Validation.Findings.Select(ToFindingDto).ToList(),
            Lines = preview.Lines.Select(l => new PayrollPreviewLineDto
            {
                EmployeeId = l.EmployeeId, EmployeeNumber = l.EmployeeNumber, EmployeeName = l.EmployeeName,
                Gross = l.Gross, Deductions = l.Deductions, Net = l.Net, HasErrors = l.HasErrors,
            }).ToList(),
        });
    }

    [HttpGet("runs")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<List<PayrollRunListItem>>>> Runs(CancellationToken ct)
    {
        var runs = await _db.PayrollRuns.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return OkResponse(runs.Select(ToListItem).ToList());
    }

    [HttpGet("runs/{id:guid}")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Run(Guid id, CancellationToken ct)
    {
        var summary = await _runRead.GetSummaryAsync(id, ct);
        return summary is null
            ? NotFound(ApiResponse<PayrollRunSummaryDto>.Fail("Run not found"))
            : OkResponse(ToSummaryDto(summary));
    }

    [HttpPost("runs")]
    [RequirePermission("Payroll.Run")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> CreateRun([FromBody] CreateRunRequest req, CancellationToken ct)
    {
        var run = await _runEngine.CreateAsync(req.DefinitionId, PayrollPeriod.Monthly(req.Year, req.Month), ct);
        return CreatedResponse(ToSummaryDto((await _runRead.GetSummaryAsync(run.Id, ct))!));
    }

    [HttpPost("runs/{id:guid}/calculate")]
    [RequirePermission("Payroll.Run")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Calculate(Guid id, CancellationToken ct)
    {
        await _runEngine.CalculateAsync(id, ct);
        return OkResponse(ToSummaryDto((await _runRead.GetSummaryAsync(id, ct))!));
    }

    [HttpPost("runs/{id:guid}/validate")]
    [RequirePermission("Payroll.Run")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Validate(Guid id, CancellationToken ct)
    {
        await _runEngine.ValidateAsync(id, ct);
        return OkResponse(ToSummaryDto((await _runRead.GetSummaryAsync(id, ct))!));
    }

    [HttpPost("runs/{id:guid}/submit")]
    [RequirePermission("Payroll.Run")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Submit(Guid id, CancellationToken ct)
    {
        await _runEngine.SubmitForApprovalAsync(id, ct);
        return OkResponse(ToSummaryDto((await _runRead.GetSummaryAsync(id, ct))!));
    }

    [HttpPost("runs/{id:guid}/approve")]
    [RequirePermission("Payroll.Approve")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Approve(Guid id, CancellationToken ct)
    {
        await _runEngine.ApproveAsync(id, ct);
        return OkResponse(ToSummaryDto((await _runRead.GetSummaryAsync(id, ct))!));
    }

    [HttpPost("runs/{id:guid}/execute")]
    [RequirePermission("Payroll.Lock")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Execute(Guid id, CancellationToken ct)
    {
        await _scheduler.EnqueueAsync(id, ct);
        return OkResponse(ToSummaryDto((await _runRead.GetSummaryAsync(id, ct))!));
    }

    [HttpPost("runs/{id:guid}/cancel")]
    [RequirePermission("Payroll.Run")]
    public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Cancel(Guid id, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        await _runEngine.CancelAsync(id, string.IsNullOrWhiteSpace(req.Reason) ? "Cancelled" : req.Reason, ct);
        return OkResponse(ToSummaryDto((await _runRead.GetSummaryAsync(id, ct))!));
    }

    // ---- payroll types ----

    [HttpGet("types")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<List<PayrollTypeListItem>>>> Types(CancellationToken ct)
    {
        var list = await _db.PayrollDefinitions.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new PayrollTypeListItem
            {
                Id = d.Id, Code = d.Code, Name = d.Name, NameAr = d.NameAr, CategoryId = d.CategoryId,
                Status = d.Status.ToString(), CurrentVersionId = d.CurrentVersionId,
                VersionCount = d.Versions.Count,
            }).ToListAsync(ct);
        return OkResponse(list);
    }

    [HttpGet("types/{id:guid}")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<PayrollTypeDetailDto>>> Type(Guid id, CancellationToken ct)
    {
        var d = await _db.PayrollDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("PayrollType", id);
        var versions = await _db.PayrollDefinitionVersions.AsNoTracking()
            .Where(v => v.PayrollDefinitionId == id).OrderBy(v => v.VersionNumber)
            .Select(v => new PayrollVersionDto
            {
                Id = v.Id, VersionNumber = v.VersionNumber, Status = v.Status.ToString(),
                CutoffDay = v.CutoffDay, DayBasis = v.DayBasis.ToString(), ClosingDate = v.ClosingDate,
                PaymentDate = v.PaymentDate, CarryToNextPeriod = v.CarryToNextPeriod,
                DefaultExportFormatId = v.DefaultExportFormatId, PaymentMethodId = v.PaymentMethodId,
                ApprovalWorkflowId = v.ApprovalWorkflowId, RuleSetVersionId = v.RuleSetVersionId,
                Currency = v.Currency, Frequency = v.Frequency.ToString(),
                EffectiveFrom = v.EffectiveFrom, EffectiveTo = v.EffectiveTo,
                SelectionScopeJson = v.SelectionScopeJson, CalcSettingsJson = v.CalcSettingsJson,
                PaymentMethodScopeJson = v.PaymentMethodScopeJson,
            }).ToListAsync(ct);
        return OkResponse(new PayrollTypeDetailDto
        {
            Id = d.Id, Code = d.Code, Name = d.Name, NameAr = d.NameAr, CategoryId = d.CategoryId,
            Status = d.Status.ToString(), CurrentVersionId = d.CurrentVersionId, Versions = versions,
        });
    }

    [HttpPost("types")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateType([FromBody] CreateTypeRequest req, CancellationToken ct)
        => CreatedResponse(await _types.CreateTypeAsync(new CreatePayrollTypeArgs(req.Code, req.Name, req.NameAr, req.CategoryId), ct));

    [HttpPut("types/{id:guid}")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateHeader(Guid id, [FromBody] UpdateHeaderRequest req, CancellationToken ct)
    {
        var status = Enum.TryParse<PayrollDefinitionStatus>(req.Status, out var s) ? s : PayrollDefinitionStatus.Active;
        await _types.UpdateHeaderAsync(id, req.Name, req.NameAr, req.CategoryId, status, ct);
        return OkResponse(true);
    }

    [HttpPost("types/{id:guid}/versions")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateVersion(Guid id, CancellationToken ct)
        => CreatedResponse(await _types.CreateDraftVersionAsync(id, ct));

    [HttpPut("types/{id:guid}/versions/{vid:guid}")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateVersion(Guid id, Guid vid, [FromBody] UpdateVersionRequest req, CancellationToken ct)
    {
        await _types.UpdateDraftVersionAsync(id, vid, new UpdatePayrollVersionArgs
        {
            CutoffDay = req.CutoffDay,
            DayBasis = Enum.TryParse<DayBasis>(req.DayBasis, out var b) ? b : null,
            ClosingDate = req.ClosingDate, PaymentDate = req.PaymentDate, CarryToNextPeriod = req.CarryToNextPeriod,
            DefaultExportFormatId = req.DefaultExportFormatId, PaymentMethodId = req.PaymentMethodId,
            ApprovalWorkflowId = req.ApprovalWorkflowId, RuleSetVersionId = req.RuleSetVersionId,
            Currency = req.Currency,
            Frequency = Enum.TryParse<PayFrequency>(req.Frequency, out var f) ? f : null,
            SelectionScopeJson = req.SelectionScopeJson, CalcSettingsJson = req.CalcSettingsJson,
            PaymentMethodScopeJson = req.PaymentMethodScopeJson,
        }, ct);
        return OkResponse(true);
    }

    [HttpPost("types/{id:guid}/versions/{vid:guid}/clone")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<Guid>>> CloneVersion(Guid id, Guid vid, CancellationToken ct)
        => CreatedResponse(await _types.CloneVersionAsync(id, vid, ct));

    [HttpPost("types/{id:guid}/versions/{vid:guid}/publish")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<bool>>> PublishVersion(Guid id, Guid vid, CancellationToken ct)
    {
        await _types.PublishVersionAsync(id, vid, ct);
        return OkResponse(true);
    }

    [HttpPost("types/{id:guid}/versions/{vid:guid}/simulate")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<PayrollPreviewDto>>> Simulate(Guid id, Guid vid, [FromBody] SimulateRequest req, CancellationToken ct)
    {
        var preview = await _types.SimulateAsync(id, vid, req.Year, req.Month, ct);
        return OkResponse(new PayrollPreviewDto
        {
            EmployeeCount = preview.EmployeeCount, GrossTotal = preview.GrossTotal,
            DeductionTotal = preview.DeductionTotal, NetTotal = preview.NetTotal, Currency = preview.Currency,
            IsValid = preview.Validation.IsValid,
            Findings = preview.Validation.Findings.Select(ToFindingDto).ToList(),
            Lines = preview.Lines.Select(l => new PayrollPreviewLineDto
            {
                EmployeeId = l.EmployeeId, EmployeeNumber = l.EmployeeNumber, EmployeeName = l.EmployeeName,
                Gross = l.Gross, Deductions = l.Deductions, Net = l.Net, HasErrors = l.HasErrors,
            }).ToList(),
        });
    }

    // ---- scope ----

    [HttpGet("scope/dimensions")]
    [RequirePermission("Payroll.View")]
    public ActionResult<ApiResponse<List<ScopeDimensionDto>>> ScopeDimensions()
        => OkResponse(_scope.Dimensions().Select(d => new ScopeDimensionDto
        {
            Key = d.Key, NameEn = d.NameEn, NameAr = d.NameAr,
            ValueSourceKind = d.ValueSource.Kind.ToString(), ValueSourceRef = d.ValueSource.Reference,
            IsAvailable = d.IsAvailable, UnavailableNote = d.UnavailableNote,
        }).ToList());

    [HttpPost("scope/resolve")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<ResolveScopeResult>>> ResolveScope([FromBody] ResolveScopeRequest req, CancellationToken ct)
    {
        var resolution = await _scope.ResolveAsync(SelectionScopeJson.Parse(req.ScopeJson), ct);
        return OkResponse(new ResolveScopeResult
        {
            IncludedCount = resolution.IncludedEmployeeIds.Count,
            ExcludedCount = resolution.ExcludedByScope.Count,
            Warnings = resolution.Warnings.ToList(),
        });
    }

    // ---- transactions ----

    [HttpGet("transactions")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<List<PayrollTransactionDto>>>> ListTransactions(
        [FromQuery] PayrollTransactionKind? kind, [FromQuery] Guid? employeeId,
        [FromQuery] int? periodYear, [FromQuery] int? periodMonth, [FromQuery] Guid? typeId,
        [FromQuery] PayrollTransactionStatus? status, [FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo,
        CancellationToken ct)
    {
        var rows = await _transactions.ListAsync(
            new PayrollTransactionFilter(kind, employeeId, periodYear, periodMonth, typeId, status, dateFrom, dateTo), ct);
        return OkResponse(rows.ToList());
    }

    [HttpGet("transactions/{id:guid}")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> GetTransaction(Guid id, CancellationToken ct)
    {
        var dto = await _transactions.GetAsync(id, ct);
        return dto is null ? NotFound(ApiResponse<PayrollTransactionDto>.Fail("غير موجود")) : OkResponse(dto);
    }

    [HttpPost("transactions")]
    [RequirePermission("Payroll.Create")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> CreateTransaction(
        [FromBody] CreateTransactionRequest req, CancellationToken ct)
    {
        var id = await _transactions.CreateAsync(new CreatePayrollTransactionArgs(
            req.Kind, req.EmployeeId, req.TypeId, req.Amount, req.EffectiveDate, req.TransactionDate,
            req.IsRecurring, req.RecurrenceEndDate, req.Notes, req.AttachmentFileId, req.SubmitImmediately), ct);
        return CreatedResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpPut("transactions/{id:guid}")]
    [RequirePermission("Payroll.Edit")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> UpdateTransaction(
        Guid id, [FromBody] UpdateTransactionRequest req, CancellationToken ct)
    {
        await _transactions.UpdateAsync(id, new UpdatePayrollTransactionArgs(
            req.TypeId, req.Amount, req.EffectiveDate, req.TransactionDate,
            req.IsRecurring, req.RecurrenceEndDate, req.Notes, req.AttachmentFileId), ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpPost("transactions/{id:guid}/submit")]
    [RequirePermission("Payroll.Edit")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> SubmitTransaction(Guid id, CancellationToken ct)
    {
        await _transactions.SubmitAsync(id, ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpPost("transactions/{id:guid}/approve")]
    [RequirePermission("Payroll.Approve")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> ApproveTransaction(Guid id, CancellationToken ct)
    {
        await _transactions.ApproveAsync(id, ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpPost("transactions/{id:guid}/reject")]
    [RequirePermission("Payroll.Approve")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> RejectTransaction(
        Guid id, [FromBody] RejectTransactionRequest req, CancellationToken ct)
    {
        await _transactions.RejectAsync(id, req.Reason, ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpPost("transactions/{id:guid}/cancel")]
    [RequirePermission("Payroll.Edit")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> CancelTransaction(
        Guid id, [FromBody] CancelTransactionRequest req, CancellationToken ct)
    {
        await _transactions.CancelAsync(id, req.Reason, ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpPost("transactions/{id:guid}/attachment")]
    [RequirePermission("Payroll.Edit")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> SetTransactionAttachment(
        Guid id, [FromBody] SetAttachmentRequest req, CancellationToken ct)
    {
        await _transactions.SetAttachmentAsync(id, req.FileId, ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpDelete("transactions/{id:guid}")]
    [RequirePermission("Payroll.Delete")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTransaction(Guid id, CancellationToken ct)
    {
        await _transactions.DeleteAsync(id, ct);
        return OkResponse<object>(new { deleted = true });
    }

    [HttpPost("transactions/{id:guid}/reverse")]
    [RequirePermission("Payroll.Approve")]
    public async Task<ActionResult<ApiResponse<PayrollTransactionDto>>> ReverseTransaction(
        Guid id, [FromBody] ReverseTransactionRequest req, CancellationToken ct)
    {
        await _reversals.ReverseAsync(id, req.Reason, req.CreateCorrection, req.CorrectedAmount, ct);
        return OkResponse(await _transactions.GetAsync(id, ct));
    }

    [HttpGet("transactions/impact-preview")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<TransactionImpactDto>>> TransactionImpactPreview(
        [FromQuery] DateTime effectiveDate, CancellationToken ct)
    {
        // Use the most recent payroll definition version's cutoff (the standard MONTHLY cycle in 2C).
        var version = await _db.PayrollDefinitionVersions.AsNoTracking()
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new { v.CutoffDay, v.CarryToNextPeriod })
            .FirstOrDefaultAsync(ct);
        var cutoffDay = version?.CutoffDay ?? 27;
        var carry = version?.CarryToNextPeriod ?? true;
        var (year, month) = PayrollPeriodResolver.Resolve(effectiveDate, cutoffDay, carry);
        var carried = carry && effectiveDate.Day > cutoffDay;
        return OkResponse(new TransactionImpactDto(year, month, cutoffDay, carried));
    }

    // ---- attendance deductions (2D) ----

    [HttpPost("attendance-deductions/sync")]
    [RequirePermission("Payroll.Configure")]
    public async Task<ActionResult<ApiResponse<AttendancePayrollSyncReportDto>>> SyncAttendanceDeductions(
        [FromBody] SyncAttendanceDeductionsRequest req, CancellationToken ct)
    {
        var versionId = await ResolveVersionAsync(req.DefinitionId, ct);
        var version = await _db.PayrollDefinitionVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new NotFoundException("PayrollDefinitionVersion", versionId);

        IReadOnlyCollection<Guid> employeeIds = req.EmployeeIds is { Count: > 0 }
            ? req.EmployeeIds
            : (await _scope.ResolveAsync(SelectionScopeJson.Parse(version.SelectionScopeJson), ct)).IncludedEmployeeIds.ToList();

        var report = await _attendanceSync.SyncAsync(version, PayrollPeriod.Monthly(req.Year, req.Month), employeeIds, ct: ct);
        return OkResponse(new AttendancePayrollSyncReportDto(
            report.Created, report.Updated, report.Removed, report.SkippedPosted, report.TotalProcessed),
            $"Synced {report.TotalProcessed} attendance line(s).");
    }

    [HttpGet("transactions/{id:guid}/attendance-breakdown")]
    [RequirePermission("Payroll.View")]
    public async Task<ActionResult<ApiResponse<List<AttendanceBreakdownDto>>>> AttendanceBreakdown(Guid id, CancellationToken ct)
    {
        var rows = await _db.PayrollTransactionAttendanceReferences.AsNoTracking()
            .Where(r => r.PayrollTransactionId == id)
            .OrderBy(r => r.Date)
            .Select(r => new AttendanceBreakdownDto(
                r.AttendanceRecordId, r.Date, r.PenaltyKind.ToString(), r.Minutes, r.Days, r.AmountContribution))
            .ToListAsync(ct);
        return OkResponse(rows);
    }

    // ---- helpers ----

    private async Task<Guid> ResolveVersionAsync(Guid definitionId, CancellationToken ct)
    {
        var versionId = await _db.PayrollDefinitions.Where(d => d.Id == definitionId)
            .Select(d => d.CurrentVersionId).FirstOrDefaultAsync(ct);
        return versionId ?? throw new ConflictException("This payroll definition has no published version.");
    }

    /// <summary>Maps the application-layer summary record to the HTTP DTO.
    /// Kept as a thin mapper here so the DTO is decoupled from the service record type.</summary>
    private static PayrollRunSummaryDto ToSummaryDto(PayrollRunSummary s) => new()
    {
        Id = s.Id, RunNumber = s.RunNumber, PeriodStart = s.PeriodStart, PeriodEnd = s.PeriodEnd,
        TargetPeriodYear = s.TargetPeriodYear, TargetPeriodMonth = s.TargetPeriodMonth,
        State = s.State, Currency = s.Currency,
        Kpis = new RunKpisDto
        {
            IncludedEmployees = s.Kpis.IncludedEmployees, ExcludedEmployees = s.Kpis.ExcludedEmployees,
            Gross = s.Kpis.Gross, Deductions = s.Kpis.Deductions, Net = s.Kpis.Net,
            TransactionsConsumed = s.Kpis.TransactionsConsumed, ApprovedNotConsumed = s.Kpis.ApprovedNotConsumed,
        },
        Calc = new RunCalcMetaDto
        {
            Version = s.Calc.Version, At = s.Calc.At, ByUserId = s.Calc.ByUserId, ByUserName = s.Calc.ByUserName,
        },
        CalculationStatus = s.CalculationStatus,
        Timeline = s.Timeline.Select(t => new RunTransitionDto { FromState = t.FromState, ToState = t.ToState, At = t.At, Reason = t.Reason }).ToList(),
    };

    private static PayrollRunListItem ToListItem(PayrollRun r) => new()
    {
        Id = r.Id, RunNumber = r.RunNumber, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd,
        State = r.State.ToString(), Currency = r.Currency, EmployeeCount = r.EmployeeCount,
        GrossTotal = r.GrossTotal, DeductionTotal = r.DeductionTotal, NetTotal = r.NetTotal, CreatedAt = r.CreatedAt,
    };

    private static ValidationFindingDto ToFindingDto(ValidationFinding f) => new()
    {
        Code = f.Code,
        Severity = f.Severity.ToString(),
        Message = f.Message,
        SuggestedAction = f.SuggestedAction,
        TargetModule = f.TargetModule,
        TargetScreen = f.TargetScreen,
        RelatedEntityType = f.RelatedEntityType,
        RelatedEntityId = f.RelatedEntityId,
        EmployeeId = f.EmployeeId,
        EmployeeName = f.EmployeeName,
    };
}
