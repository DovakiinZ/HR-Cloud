using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Attendance;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: record an approved attendance permission (استئذان) as a durable excuse row, and
/// enforce the tenant's monthly cap. The window∩shift minutes are snapshotted as
/// <see cref="AttendancePermission.ExcusedMinutes"/> (the value tallied against the cap); the calc
/// engine later waives the late/early minutes overlapping the window. Over-cap under Block mode throws
/// (the completion transaction rolls back and the request lands in CompletionFailed); under Warn mode
/// the row is still written and the summary flags it.</summary>
public sealed class AttendancePermissionCreateExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IShiftResolver _resolver;

    public AttendancePermissionCreateExecutor(ApplicationDbContext db, IShiftResolver resolver)
    {
        _db = db;
        _resolver = resolver;
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

        // Idempotency: one permission row per approved request instance.
        var already = await _db.AttendancePermissions.AnyAsync(
            p => p.EmployeeId == ctx.EmployeeId && p.RequestInstanceId == ctx.RequestInstanceId, ct);
        if (already)
            return EffectExecutionResult.Skip("AlreadyApplied",
                targetEntityType: nameof(AttendancePermission),
                summary: $"Attendance permission for {date:yyyy-MM-dd} already recorded by this request.");

        // Snapshot the window∩shift minutes (falls back to raw window length when no shift is assigned).
        var window = new PermissionWindow(fromMin, toMin);
        var shift = await ResolveShiftAsync(ctx.EmployeeId, date, ct);
        var excused = PermissionMath.WindowMinutesWithinShift(shift, new[] { window });

        // Monthly cap — tally the employee's approved permissions in the calendar month of `date`.
        var policy = await _db.AttendancePolicies.AsNoTracking()
            .Where(x => x.IsActive).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(ct);
        var monthStart = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        var priorMinutes = await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.EmployeeId == ctx.EmployeeId && p.Date >= monthStart && p.Date < monthEnd)
            .Select(p => p.ExcusedMinutes).ToListAsync(ct);

        var decision = AttendancePermissionCap.Evaluate(policy, priorMinutes.Count, priorMinutes.Sum(), excused);
        if (decision.IsBlocked)
            throw new NonRetryableEffectException(decision.ReasonEn ?? "Monthly attendance-permission cap exceeded.");

        var row = new AttendancePermission
        {
            EmployeeId = ctx.EmployeeId,
            Date = date,
            FromMinutes = fromMin,
            ToMinutes = toMin,
            ExcusedMinutes = excused,
            Reason = ctx.Str("reason"),
            RequestInstanceId = ctx.RequestInstanceId,
            Source = AttendanceSources.AttendancePermission,
            CreatedByUserId = ctx.ActorUserId,
        };
        _db.AttendancePermissions.Add(row);

        var summary = decision.IsWarning
            ? $"Attendance permission recorded for {date:yyyy-MM-dd} ({excused}m) — over the monthly cap (warning)."
            : $"Attendance permission recorded for {date:yyyy-MM-dd} ({excused}m excused).";

        return EffectExecutionResult.Ok(
            targetEntityType: nameof(AttendancePermission), targetRecordId: row.Id,
            after: new { row.Date, row.FromMinutes, row.ToMinutes, row.ExcusedMinutes, capWarning = decision.IsWarning },
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
