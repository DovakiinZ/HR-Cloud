using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Permissions;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Notifications;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.DTOs;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: apply an approved attendance correction by recomputing the day from the corrected
/// punches (via IAttendanceService), instead of blindly zeroing penalties.</summary>
public sealed class AttendanceCorrectionExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IAttendanceService _attendance;
    private readonly IPayrollPeriodGuard _payrollGuard;
    private readonly IPermissionResolver _permissions;

    public AttendanceCorrectionExecutor(
        ApplicationDbContext db,
        IAttendanceService attendance,
        IPayrollPeriodGuard payrollGuard,
        IPermissionResolver permissions)
    {
        _db = db;
        _attendance = attendance;
        _payrollGuard = payrollGuard;
        _permissions = permissions;
    }

    public string EffectType => EffectTypes.AttendanceCorrect;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind((ctx.Date("date") ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var reason = ctx.Str("reason");
        var checkIn = ctx.Str("checkIn");
        var checkOut = ctx.Str("checkOut");

        // Validate: reason required; ≥1 punch; each provided punch is HH:mm.
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException(new[] { new ValidationFailure("reason", "سبب التصحيح مطلوب / Reason is required.") });
        if (!PunchTime.HasValue(checkIn) && !PunchTime.HasValue(checkOut))
            throw new ValidationException(new[] { new ValidationFailure("punch", "يجب إدخال وقت الحضور أو الانصراف / At least one punch is required.") });
        if (!PunchTime.IsValid(checkIn) || !PunchTime.IsValid(checkOut))
            throw new ValidationException(new[] { new ValidationFailure("punch", "صيغة الوقت يجب أن تكون HH:mm / Punch times must be HH:mm.") });

        var already = await _db.AttendanceRecords.AnyAsync(a =>
            a.EmployeeId == ctx.EmployeeId && a.Date == date
            && a.Source == AttendanceSources.AttendanceCorrection
            && a.ReferenceId == ctx.RequestInstanceId, ct);
        if (already)
            return EffectExecutionResult.Skip("AlreadyApplied",
                targetEntityType: "AttendanceRecord",
                summary: $"Attendance for {date:yyyy-MM-dd} already corrected by this request.");

        // Finalized-payroll guard: block unless the actor holds Payroll.Run.Amend.
        bool periodFinalized = false;
        try { await _payrollGuard.EnsurePeriodOpenForAsync(ctx.EmployeeId, date, ct); }
        catch (PayrollPeriodClosedException) { periodFinalized = true; }

        if (periodFinalized)
        {
            var perms = ctx.ActorUserId is { } uid
                ? await _permissions.ResolveAsync(uid, ct)
                : (IReadOnlyList<string>)Array.Empty<string>();
            if (!perms.Contains("Payroll.Run.Amend"))
                throw new ValidationException(new[]
                {
                    new ValidationFailure("payrollPeriod",
                        $"لا يمكن تعديل فترة رواتب مقفلة ({date:yyyy-MM}) دون صلاحية / " +
                        $"Payroll period {date:yyyy-MM} is finalized; correcting it requires payroll-amend authorization.")
                });
        }

        var existing = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == ctx.EmployeeId && a.Date == date, ct);

        Guid targetId;
        if (existing is not null)
        {
            await _attendance.CorrectAsync(existing.Id,
                new CorrectAttendanceRequest { CheckIn = checkIn, CheckOut = checkOut, Reason = reason }, ct);
            targetId = existing.Id;
        }
        else
        {
            targetId = await _attendance.AddManualPunchAsync(
                new ManualPunchRequest { EmployeeId = ctx.EmployeeId, Date = date, CheckIn = checkIn, CheckOut = checkOut, Notes = reason }, ct);
        }

        // Stamp provenance so the idempotency guard matches on re-run.
        var applied = await _db.AttendanceRecords.FirstAsync(a => a.Id == targetId, ct);
        applied.Source = AttendanceSources.AttendanceCorrection;
        applied.ReferenceId = ctx.RequestInstanceId;
        await _db.SaveChangesAsync(ct);

        // If the period was finalized but the actor was authorized, emit a payroll-adjustment signal.
        if (periodFinalized && ctx.ActorUserId is { } signalUser)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = signalUser,
                TitleAr = "تصحيح حضور بعد إقفال الرواتب",
                TitleEn = "Attendance corrected after payroll finalized",
                BodyAr = $"تم تصحيح حضور الموظف ليوم {date:yyyy-MM-dd} بعد إقفال رواتب الفترة (الطلب {ctx.RequestNumber}). قد يلزم تسوية في الرواتب: تأخير {applied.LateMinutes}د، نقص {applied.ShortageMinutes}د.",
                BodyEn = $"Attendance for {date:yyyy-MM-dd} was corrected after payroll was finalized (request {ctx.RequestNumber}). A payroll adjustment may be required: late {applied.LateMinutes}m, shortage {applied.ShortageMinutes}m.",
                Category = "PayrollAdjustmentNeeded",
                EntityId = ctx.RequestInstanceId,
                Link = "/payroll",
                IsRead = false,
            });
            await _db.SaveChangesAsync(ct);
        }

        return EffectExecutionResult.Ok(
            targetEntityType: "AttendanceRecord", targetRecordId: targetId,
            after: new { date, applied.LateMinutes, applied.ShortageMinutes, applied.Status },
            summary: $"Attendance recomputed for {date:yyyy-MM-dd}: late {applied.LateMinutes}m, shortage {applied.ShortageMinutes}m");
    }
}
