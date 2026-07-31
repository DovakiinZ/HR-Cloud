using HR.Domain.Enums;

namespace HR.Domain.Engines.Attendance;

/// <summary>What the monthly attendance-permission cap says about one more permission.</summary>
public enum AttendancePermissionCapOutcome
{
    /// <summary>Within cap (or no cap configured).</summary>
    Allowed = 0,
    /// <summary>Over cap, but the policy only warns — the permission is still recorded.</summary>
    Warn = 1,
    /// <summary>Over cap and the policy blocks — the approval must be rejected.</summary>
    Block = 2,
}

/// <summary>Verdict for adding one attendance permission, with a bilingual reason when it is not allowed.</summary>
public readonly record struct AttendancePermissionCapDecision(
    AttendancePermissionCapOutcome Outcome, string? ReasonAr, string? ReasonEn)
{
    public bool IsBlocked => Outcome == AttendancePermissionCapOutcome.Block;
    public bool IsWarning => Outcome == AttendancePermissionCapOutcome.Warn;

    public static readonly AttendancePermissionCapDecision Allowed =
        new(AttendancePermissionCapOutcome.Allowed, null, null);
}

/// <summary>
/// Pure evaluation of the tenant's monthly attendance-permission (استئذان) cap. Given the policy and
/// what an employee has already used this calendar month, decides whether one more permission is
/// Allowed, Warn, or Block. A null policy or unset caps are unlimited. The count cap counts approved
/// permissions; the minutes cap tallies their window∩shift excused minutes. Either cap breaching is
/// enough; <see cref="PermissionCapMode"/> turns a breach into a Warn or a Block.
/// </summary>
public static class AttendancePermissionCap
{
    public static AttendancePermissionCapDecision Evaluate(
        AttendancePolicy? policy, int approvedCountThisMonth, int approvedMinutesThisMonth, int newExcusedMinutes)
    {
        var maxCount = policy?.PermissionMaxPerMonth;
        var maxMinutes = policy?.PermissionMaxMinutesPerMonth;
        if (maxCount is null && maxMinutes is null) return AttendancePermissionCapDecision.Allowed;

        var overCount = maxCount is int c && approvedCountThisMonth + 1 > c;
        var overMinutes = maxMinutes is int m && approvedMinutesThisMonth + newExcusedMinutes > m;
        if (!overCount && !overMinutes) return AttendancePermissionCapDecision.Allowed;

        var outcome = policy!.PermissionCapMode == PermissionCapMode.Block
            ? AttendancePermissionCapOutcome.Block
            : AttendancePermissionCapOutcome.Warn;

        string ar, en;
        if (overCount)
        {
            var attempted = approvedCountThisMonth + 1;
            ar = $"تجاوز الحد الشهري لعدد الاستئذانات ({attempted}/{maxCount}).";
            en = $"Exceeds the monthly permission count cap ({attempted}/{maxCount}).";
        }
        else
        {
            var attempted = approvedMinutesThisMonth + newExcusedMinutes;
            ar = $"تجاوز الحد الشهري لدقائق الاستئذان ({attempted}/{maxMinutes} دقيقة).";
            en = $"Exceeds the monthly permission minutes cap ({attempted}/{maxMinutes} min).";
        }
        return new AttendancePermissionCapDecision(outcome, ar, en);
    }
}
