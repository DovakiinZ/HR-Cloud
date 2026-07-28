using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Attendance;
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

    public AttendanceCorrectionExecutor(ApplicationDbContext db, IAttendanceService attendance)
    { _db = db; _attendance = attendance; }

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

        // Stamp provenance so the idempotency guard (Task 4) matches on re-run.
        var applied = await _db.AttendanceRecords.FirstAsync(a => a.Id == targetId, ct);
        applied.Source = AttendanceSources.AttendanceCorrection;
        applied.ReferenceId = ctx.RequestInstanceId;
        await _db.SaveChangesAsync(ct);

        return EffectExecutionResult.Ok(
            targetEntityType: "AttendanceRecord", targetRecordId: targetId,
            after: new { date, applied.LateMinutes, applied.ShortageMinutes, applied.Status },
            summary: $"Attendance recomputed for {date:yyyy-MM-dd}: late {applied.LateMinutes}m, shortage {applied.ShortageMinutes}m");
    }
}
