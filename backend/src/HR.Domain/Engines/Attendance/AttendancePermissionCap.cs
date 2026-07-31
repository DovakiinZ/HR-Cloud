using HR.Domain.Enums;

namespace HR.Domain.Engines.Attendance;

/// <summary>What a per-type (or policy-fallback) attendance-permission limit evaluation yields.</summary>
public enum AttendancePermissionCapOutcome
{
    /// <summary>Within every limit (or no limit configured).</summary>
    Allowed = 0,
    /// <summary>Over a limit, but the type says only warn — the permission is still recorded.</summary>
    Warn = 1,
    /// <summary>Over a limit and the type blocks — the approval must be rejected.</summary>
    Block = 2,
    /// <summary>Over a limit and the type requires a manager override reason to proceed.</summary>
    RequireOverride = 3,
}

/// <summary>Fully-resolved limit set for one permission type + optional policy fallback.
/// All limits are nullable; null means unlimited on that dimension.</summary>
public readonly record struct PermissionLimitSet(
    int? MaxMinutesPerRequest,
    int? MaxMinutesPerDay,
    int? MaxMinutesPerMonth,
    int? MaxRequestsPerDay,
    int? MaxRequestsPerMonth,
    PermissionExceedBehavior Behavior);

/// <summary>What the employee has already used for this permission type today and this calendar month.</summary>
public readonly record struct PermissionUsageTally(
    int UsedMinutesDay,
    int UsedMinutesMonth,
    int UsedRequestsDay,
    int UsedRequestsMonth);

/// <summary>Verdict for adding one attendance permission.</summary>
public readonly record struct AttendancePermissionCapDecision(
    AttendancePermissionCapOutcome Outcome, string? ReasonAr, string? ReasonEn)
{
    public bool IsBlocked       => Outcome == AttendancePermissionCapOutcome.Block;
    public bool IsWarning       => Outcome == AttendancePermissionCapOutcome.Warn;
    public bool RequiresOverride => Outcome == AttendancePermissionCapOutcome.RequireOverride;

    public static readonly AttendancePermissionCapDecision Allowed =
        new(AttendancePermissionCapOutcome.Allowed, null, null);
}

/// <summary>
/// Pure evaluation of per-type attendance-permission limits.
/// Given a fully-resolved <see cref="PermissionLimitSet"/> (computed by <c>PermissionLimitResolver</c> in
/// HR.Application) and the employee's current usage tally, decides whether one more permission is
/// Allowed, Warn, Block, or RequireOverride.
/// <para>Breach rule: <c>used + newRequestMinutes &gt; limit</c> for minute dims;
/// <c>usedRequests + 1 &gt; limit</c> for count dims;
/// per-request compares <paramref name="newRequestMinutes"/> alone (no accumulation).
/// Exactly-at-cap is Allowed.</para>
/// <para>The first breached dimension's name appears in the bilingual reason.</para>
/// </summary>
public static class AttendancePermissionCap
{
    /// <param name="limits">Resolved limit set (may contain null dims → unlimited).</param>
    /// <param name="used">What the employee has already used today and this month for this type.</param>
    /// <param name="newRequestMinutes">In-shift excused minutes for this request.</param>
    public static AttendancePermissionCapDecision Evaluate(
        PermissionLimitSet limits, PermissionUsageTally used, int newRequestMinutes)
    {
        // Per-request minutes limit (compared to just this request, no accumulation).
        if (limits.MaxMinutesPerRequest is int maxPerReq && newRequestMinutes > maxPerReq)
        {
            var ar = $"مدة الاستئذان ({newRequestMinutes} دقيقة) تتجاوز الحد الأقصى لكل طلب ({maxPerReq} دقيقة).";
            var en = $"Permission duration ({newRequestMinutes} min) exceeds the per-request limit ({maxPerReq} min).";
            return Decision(limits.Behavior, ar, en);
        }

        // Daily minutes.
        if (limits.MaxMinutesPerDay is int maxDay && used.UsedMinutesDay + newRequestMinutes > maxDay)
        {
            var attempted = used.UsedMinutesDay + newRequestMinutes;
            var ar = $"تجاوز الحد اليومي لدقائق الاستئذان ({attempted}/{maxDay} دقيقة).";
            var en = $"Exceeds the daily permission minutes cap ({attempted}/{maxDay} min).";
            return Decision(limits.Behavior, ar, en);
        }

        // Monthly minutes.
        if (limits.MaxMinutesPerMonth is int maxMonth && used.UsedMinutesMonth + newRequestMinutes > maxMonth)
        {
            var attempted = used.UsedMinutesMonth + newRequestMinutes;
            var ar = $"تجاوز الحد الشهري لدقائق الاستئذان ({attempted}/{maxMonth} دقيقة).";
            var en = $"Exceeds the monthly permission minutes cap ({attempted}/{maxMonth} min).";
            return Decision(limits.Behavior, ar, en);
        }

        // Daily request count.
        if (limits.MaxRequestsPerDay is int maxReqDay && used.UsedRequestsDay + 1 > maxReqDay)
        {
            var attempted = used.UsedRequestsDay + 1;
            var ar = $"تجاوز الحد اليومي لعدد الاستئذانات ({attempted}/{maxReqDay}).";
            var en = $"Exceeds the daily permission count cap ({attempted}/{maxReqDay}).";
            return Decision(limits.Behavior, ar, en);
        }

        // Monthly request count.
        if (limits.MaxRequestsPerMonth is int maxReqMonth && used.UsedRequestsMonth + 1 > maxReqMonth)
        {
            var attempted = used.UsedRequestsMonth + 1;
            var ar = $"تجاوز الحد الشهري لعدد الاستئذانات ({attempted}/{maxReqMonth}).";
            var en = $"Exceeds the monthly permission count cap ({attempted}/{maxReqMonth}).";
            return Decision(limits.Behavior, ar, en);
        }

        return AttendancePermissionCapDecision.Allowed;
    }

    private static AttendancePermissionCapDecision Decision(PermissionExceedBehavior behavior, string ar, string en)
    {
        var outcome = behavior switch
        {
            PermissionExceedBehavior.Warn                   => AttendancePermissionCapOutcome.Warn,
            PermissionExceedBehavior.RequireApprovalOverride => AttendancePermissionCapOutcome.RequireOverride,
            _                                                => AttendancePermissionCapOutcome.Block,
        };
        return new AttendancePermissionCapDecision(outcome, ar, en);
    }
}
