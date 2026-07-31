using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Attendance;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: record an approved attendance permission (استئذان) as a durable excuse row, and
/// enforce per-type (and policy-fallback) limits. The window∩shift minutes are snapshotted as
/// <see cref="AttendancePermission.ExcusedMinutes"/>; the calc engine later waives the
/// late/early minutes overlapping the window.
/// <para>Limits are evaluated in the order: per-request → per-day-minutes → per-month-minutes →
/// per-day-count → per-month-count. The first breached limit determines the outcome:
/// Block → throws <see cref="NonRetryableEffectException"/>;
/// RequireOverride → requires a non-empty <c>overrideReason</c> in the payload, writes an
/// <see cref="AttendanceAuditLog"/>, and flags <c>capOverride=true</c>;
/// Warn → proceeds, flags <c>capWarning=true</c>.</para>
/// <para>If the permission type is unpaid and excused minutes &gt; 0, a born-Approved
/// <see cref="PayrollTransaction"/> (Kind=Deduction) is added to the unit of work using the
/// configurable wage basis from <see cref="AttendancePolicy"/>. When the payroll period is
/// already finalized, a <c>PayrollAdjustmentNeeded</c> notification is emitted instead.</para></summary>
public sealed class AttendancePermissionCreateExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IShiftResolver _resolver;
    private readonly IAttendancePermissionTypeService _types;
    private readonly IUnpaidPermissionWageResolver _wageResolver;
    private readonly IPayrollPeriodGuard _guard;

    public AttendancePermissionCreateExecutor(
        ApplicationDbContext db,
        IShiftResolver resolver,
        IAttendancePermissionTypeService types,
        IUnpaidPermissionWageResolver wageResolver,
        IPayrollPeriodGuard guard)
    {
        _db = db;
        _resolver = resolver;
        _types = types;
        _wageResolver = wageResolver;
        _guard = guard;
    }

    public string EffectType => EffectTypes.AttendanceCreatePermission;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind(
            (ctx.Date("date") ?? throw Validation("date", "تاريخ الاستئذان مطلوب / Permission date is required.")).Date,
            DateTimeKind.Utc);

        var (fromMin, toMin) = ReadWindow(ctx);
        if (toMin <= fromMin)
            throw Validation("window", "نافذة الاستئذان غير صحيحة / Permission window is invalid (end must be after start).");

        // ── Idempotency: one permission row per approved request instance ────────────────────────────
        var already = await _db.AttendancePermissions.AnyAsync(
            p => p.EmployeeId == ctx.EmployeeId && p.RequestInstanceId == ctx.RequestInstanceId, ct);
        if (already)
            return EffectExecutionResult.Skip("AlreadyApplied",
                targetEntityType: nameof(AttendancePermission),
                summary: $"Attendance permission for {date:yyyy-MM-dd} already recorded by this request.");

        // ── Resolve permission type (required; null = missing or ineligible) ─────────────────────────
        var permissionTypeId = ctx.Str("permissionTypeId");
        if (string.IsNullOrWhiteSpace(permissionTypeId))
            throw new NonRetryableEffectException(
                "permissionTypeId is required in the effect payload. / معرّف نوع الاستئذان مطلوب في الحمولة.");

        var typeCtx = await _types.ResolveForRequestAsync(ctx.EmployeeId, permissionTypeId, ct);
        if (typeCtx is null)
            throw new NonRetryableEffectException(
                $"Attendance permission type '{permissionTypeId}' not found or employee is ineligible. " +
                $"/ نوع الاستئذان غير موجود أو الموظف غير مؤهل.");

        // ── Snapshot the window∩shift minutes ───────────────────────────────────────────────────────
        var window = new PermissionWindow(fromMin, toMin);
        var shift = await ResolveShiftAsync(ctx.EmployeeId, date, ct);
        var excused = PermissionMath.WindowMinutesWithinShift(shift, new[] { window });

        // ── Load policy for monthly-dim fallback ────────────────────────────────────────────────────
        var policy = await _db.AttendancePolicies.AsNoTracking()
            .Where(x => x.IsActive).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(ct);

        // ── Tally existing per-type usage ────────────────────────────────────────────────────────────
        var tally = await _types.TallyAsync(ctx.EmployeeId, typeCtx.Item.Id, date, ct);

        // ── Evaluate limits ──────────────────────────────────────────────────────────────────────────
        var limits = PermissionLimitResolver.Resolve(typeCtx.Rules, policy);
        var decision = AttendancePermissionCap.Evaluate(limits, tally, excused);

        bool capWarning = false;
        bool capOverride = false;

        if (decision.IsBlocked)
            throw new NonRetryableEffectException(
                decision.ReasonEn ?? "Attendance permission limit exceeded.");

        if (decision.RequiresOverride)
        {
            var overrideReason = ctx.Str("overrideReason");
            if (string.IsNullOrWhiteSpace(overrideReason))
                throw new NonRetryableEffectException(
                    $"An override reason is required to exceed this permission type's limit. " +
                    $"({decision.ReasonEn}) / سبب التجاوز مطلوب لتجاوز حد نوع الاستئذان. ({decision.ReasonAr})");

            // Write audit log for the cap override.
            _db.AttendanceAuditLogs.Add(new AttendanceAuditLog
            {
                EmployeeId = ctx.EmployeeId,
                Date = date,
                Action = "PermissionCapOverride",
                DetailsAr = $"تم تجاوز حد الاستئذان: {decision.ReasonAr} — سبب التجاوز: {overrideReason}",
                DetailsEn = $"Permission cap overridden: {decision.ReasonEn} — Override reason: {overrideReason}",
                ActorUserId = ctx.ActorUserId,
                At = DateTime.UtcNow,
            });

            capOverride = true;
        }

        if (decision.IsWarning)
            capWarning = true;

        // ── Persist the permission row ────────────────────────────────────────────────────────────────
        var row = new AttendancePermission
        {
            EmployeeId = ctx.EmployeeId,
            Date = date,
            FromMinutes = fromMin,
            ToMinutes = toMin,
            ExcusedMinutes = excused,
            Reason = ctx.Str("reason"),
            RequestInstanceId = ctx.RequestInstanceId,
            PermissionTypeId = typeCtx.Item.Id,
            Source = AttendanceSources.AttendancePermission,
            CreatedByUserId = ctx.ActorUserId,
        };
        _db.AttendancePermissions.Add(row);

        // ── Unpaid deduction path ────────────────────────────────────────────────────────────────────
        bool payrollAdjustmentFlagged = false;
        if (typeCtx.Rules.Paid == false && excused > 0)
        {
            // Dedupe: skip if a PayrollTransaction for this permission row already exists.
            var alreadyDeducted = await _db.PayrollTransactions.AnyAsync(
                t => t.ReferenceType == "UnpaidPermission" && t.ReferenceId == row.Id, ct);

            if (!alreadyDeducted)
            {
                bool periodClosed = false;
                try { await _guard.EnsurePeriodOpenForAsync(ctx.EmployeeId, date, ct); }
                catch (PayrollPeriodClosedException) { periodClosed = true; }

                if (periodClosed)
                {
                    // Emit a payroll-adjustment notification; do NOT create the transaction.
                    var hoursStr = $"{excused / 60m:0.##}";
                    // We cannot know the exact amount without resolving the wage, but we compute it
                    // for the notification body.
                    var (wagePreview, divisorPreview, hoursPreview) =
                        await _wageResolver.ResolveAsync(ctx.EmployeeId, date, ct);
                    var amountPreview = UnpaidPermissionDeduction.Amount(wagePreview, excused, divisorPreview, hoursPreview);

                    _db.Notifications.Add(new Notification
                    {
                        UserId = ctx.ActorUserId ?? Guid.Empty,
                        TitleAr = "خصم استئذان غير مدفوع — فترة رواتب مقفلة",
                        TitleEn = "Unpaid Permission Deduction — Payroll Period Finalized",
                        BodyAr = $"تمت إضافة استئذان غير مدفوع ({hoursStr} ساعة، {amountPreview} ريال) للموظف في تاريخ {date:yyyy-MM-dd}، لكن فترة الرواتب مقفلة. يلزم تسوية يدوية في الرواتب.",
                        BodyEn = $"An unpaid permission ({hoursStr}h, {amountPreview:0.##} SAR) was recorded for {date:yyyy-MM-dd} but the payroll period is finalized. A manual payroll adjustment is required.",
                        Category = "PayrollAdjustmentNeeded",
                        Link = "/payroll",
                        IsRead = false,
                        EntityId = row.Id,
                    });
                    payrollAdjustmentFlagged = true;
                }
                else
                {
                    // Resolve wage and create born-Approved PayrollTransaction.
                    var (monthlyWage, divisorDays, dailyHours) =
                        await _wageResolver.ResolveAsync(ctx.EmployeeId, date, ct);
                    var amount = UnpaidPermissionDeduction.Amount(monthlyWage, excused, divisorDays, dailyHours);

                    // Resolve the UNPAID_PERMISSION DeductionType TypeId.
                    var typeId = await _db.MasterDataItems
                        .Where(m => m.ObjectType == MasterDataObjectType.DeductionType
                                    && m.Code == "UNPAID_PERMISSION")
                        .Select(m => m.Id)
                        .FirstOrDefaultAsync(ct);

                    _db.PayrollTransactions.Add(new PayrollTransaction
                    {
                        Kind = PayrollTransactionKind.Deduction,
                        EmployeeId = ctx.EmployeeId,
                        TypeId = typeId,
                        Amount = amount,
                        EffectiveDate = date,
                        TransactionDate = date,
                        TargetPeriodYear = date.Year,
                        TargetPeriodMonth = date.Month,
                        SourceModule = "AttendancePermission",
                        ReferenceType = "UnpaidPermission",
                        ReferenceId = row.Id,
                        Status = PayrollTransactionStatus.Approved,
                        Origin = PayrollTransactionOrigin.System,
                    });
                }
            }
        }

        var summary = capOverride
            ? $"Attendance permission recorded for {date:yyyy-MM-dd} ({excused}m) — cap override applied."
            : capWarning
                ? $"Attendance permission recorded for {date:yyyy-MM-dd} ({excused}m) — over the cap (warning)."
                : $"Attendance permission recorded for {date:yyyy-MM-dd} ({excused}m excused).";

        return EffectExecutionResult.Ok(
            targetEntityType: nameof(AttendancePermission), targetRecordId: row.Id,
            after: new
            {
                row.Date, row.FromMinutes, row.ToMinutes, row.ExcusedMinutes,
                row.PermissionTypeId,
                capWarning, capOverride, payrollAdjustmentFlagged,
            },
            summary: summary);
    }

    /// <summary>Read the window as minutes-from-midnight: prefer explicit fromMinutes/toMinutes, else
    /// parse fromTime/toTime "HH:mm".</summary>
    private static (int from, int to) ReadWindow(EffectContext ctx)
        => (ReadMinute(ctx, "fromMinutes", "fromTime"), ReadMinute(ctx, "toMinutes", "toTime"));

    private static int ReadMinute(EffectContext ctx, string minutesKey, string timeKey)
        => int.TryParse(ctx.Str(minutesKey), out var m) ? m
         : TimeSpan.TryParse(ctx.Str(timeKey), out var t) ? (int)t.TotalMinutes
         : 0;

    private async Task<Shift?> ResolveShiftAsync(Guid employeeId, DateTime date, CancellationToken ct)
    {
        var scope = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new EmployeeScope(e.Id, e.DepartmentId, e.BranchId, e.JobTitleId))
            .FirstOrDefaultAsync(ct);
        if (scope.Id == Guid.Empty) return null;

        var assignments = await _db.ShiftAssignments.AsNoTracking().ToListAsync(ct);
        var shifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        return _resolver.Resolve(assignments, shifts.ToDictionary(s => s.Id), scope, date);
    }

    private static ValidationException Validation(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
