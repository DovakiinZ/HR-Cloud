using HR.Application.Engines.Attendance;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>Pure-evaluator tests for per-type attendance-permission limits.
/// No DB: all tests exercise <see cref="AttendancePermissionCap.Evaluate(PermissionLimitSet,PermissionUsageTally,int)"/>
/// and <see cref="PermissionLimitResolver.Resolve"/> in isolation.</summary>
public class AttendancePermissionLimitsTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static PermissionLimitSet Limits(
        int? maxMinutesPerRequest = null,
        int? maxMinutesPerDay = null,
        int? maxMinutesPerMonth = null,
        int? maxRequestsPerDay = null,
        int? maxRequestsPerMonth = null,
        PermissionExceedBehavior behavior = PermissionExceedBehavior.Block)
        => new(maxMinutesPerRequest, maxMinutesPerDay, maxMinutesPerMonth,
               maxRequestsPerDay, maxRequestsPerMonth, behavior);

    private static PermissionUsageTally Used(
        int minutesDay = 0, int minutesMonth = 0,
        int requestsDay = 0, int requestsMonth = 0)
        => new(minutesDay, minutesMonth, requestsDay, requestsMonth);

    private static AttendancePolicy Policy(int? maxCount = null, int? maxMinutes = null,
        PermissionCapMode mode = PermissionCapMode.Block)
        => new()
        {
            PermissionMaxPerMonth = maxCount,
            PermissionMaxMinutesPerMonth = maxMinutes,
            PermissionCapMode = mode,
        };

    private static PermissionTypeRules Rules(
        int? maxMinutesPerRequest = null,
        int? maxMinutesPerDay = null,
        int? maxMinutesPerMonth = null,
        int? maxRequestsPerDay = null,
        int? maxRequestsPerMonth = null,
        PermissionExceedBehavior behavior = PermissionExceedBehavior.Block)
        => new()
        {
            MaxMinutesPerRequest = maxMinutesPerRequest,
            MaxMinutesPerDay = maxMinutesPerDay,
            MaxMinutesPerMonth = maxMinutesPerMonth,
            MaxRequestsPerDay = maxRequestsPerDay,
            MaxRequestsPerMonth = maxRequestsPerMonth,
            ExceedBehavior = behavior,
        };

    // ── evaluator tests ───────────────────────────────────────────────────────

    [Fact] // All limits null → no limit at all.
    public void Unlimited_allows()
    {
        var d = AttendancePermissionCap.Evaluate(Limits(), Used(minutesDay: 999, minutesMonth: 99_999, requestsDay: 99, requestsMonth: 999), newRequestMinutes: 120);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // Per-request cap exceeded → Block.
    public void Per_request_minutes_block()
    {
        // MaxMinutesPerRequest=60, requesting 90 → 90 > 60 → breached.
        var d = AttendancePermissionCap.Evaluate(Limits(maxMinutesPerRequest: 60), Used(), newRequestMinutes: 90);
        Assert.True(d.IsBlocked);
        Assert.False(string.IsNullOrWhiteSpace(d.ReasonEn));
        Assert.False(string.IsNullOrWhiteSpace(d.ReasonAr));
    }

    [Fact] // Per-request exactly at cap → allowed.
    public void Per_request_exactly_at_cap_is_allowed()
    {
        var d = AttendancePermissionCap.Evaluate(Limits(maxMinutesPerRequest: 60), Used(), newRequestMinutes: 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // Daily minutes cap: used 100 + new 60 > 120 → breach.
    public void Daily_minutes_cap_enforced()
    {
        var d = AttendancePermissionCap.Evaluate(Limits(maxMinutesPerDay: 120), Used(minutesDay: 100), newRequestMinutes: 60);
        Assert.True(d.IsBlocked);
    }

    [Fact] // Monthly minutes cap enforced.
    public void Monthly_minutes_cap_enforced()
    {
        var d = AttendancePermissionCap.Evaluate(Limits(maxMinutesPerMonth: 300), Used(minutesMonth: 250), newRequestMinutes: 60);
        Assert.True(d.IsBlocked);
    }

    [Fact] // Daily request count: used 2, +1 > 2 → breach.
    public void Requests_per_day_cap_enforced()
    {
        var d = AttendancePermissionCap.Evaluate(Limits(maxRequestsPerDay: 2), Used(requestsDay: 2), newRequestMinutes: 30);
        Assert.True(d.IsBlocked);
    }

    [Fact] // Monthly request count cap enforced.
    public void Requests_per_month_cap_enforced()
    {
        var d = AttendancePermissionCap.Evaluate(Limits(maxRequestsPerMonth: 5), Used(requestsMonth: 5), newRequestMinutes: 30);
        Assert.True(d.IsBlocked);
    }

    [Fact] // Exactly at daily minutes cap → allowed (cap is inclusive ceiling).
    public void Exactly_at_cap_is_allowed()
    {
        // used 60 + new 60 == 120 → NOT > 120 → Allowed.
        var d = AttendancePermissionCap.Evaluate(Limits(maxMinutesPerDay: 120), Used(minutesDay: 60), newRequestMinutes: 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // Warn behavior: breach yields Warn, not Block.
    public void Warn_behavior_yields_warn_not_block()
    {
        var d = AttendancePermissionCap.Evaluate(
            Limits(maxMinutesPerMonth: 100, behavior: PermissionExceedBehavior.Warn),
            Used(minutesMonth: 80), newRequestMinutes: 30);
        Assert.Equal(AttendancePermissionCapOutcome.Warn, d.Outcome);
        Assert.True(d.IsWarning);
        Assert.False(d.IsBlocked);
    }

    [Fact] // RequireApprovalOverride behavior → RequireOverride outcome.
    public void Override_behavior_yields_require_override()
    {
        var d = AttendancePermissionCap.Evaluate(
            Limits(maxRequestsPerDay: 1, behavior: PermissionExceedBehavior.RequireApprovalOverride),
            Used(requestsDay: 1), newRequestMinutes: 30);
        Assert.Equal(AttendancePermissionCapOutcome.RequireOverride, d.Outcome);
        Assert.True(d.RequiresOverride);
        Assert.False(d.IsBlocked);
    }

    [Fact] // First breached limit named in reason (per-request checked first).
    public void Reason_names_the_breached_limit()
    {
        var d = AttendancePermissionCap.Evaluate(
            Limits(maxMinutesPerRequest: 30, maxMinutesPerDay: 200),
            Used(), newRequestMinutes: 60);
        Assert.True(d.IsBlocked);
        Assert.Contains("request", d.ReasonEn, StringComparison.OrdinalIgnoreCase);
    }

    // ── PermissionLimitResolver tests ─────────────────────────────────────────

    [Fact] // Policy monthly dims are used as fallback when type does not set them.
    public void Resolve_falls_back_to_policy_monthly_dims_only()
    {
        var rules = Rules(); // all limits null
        var policy = Policy(maxCount: 4, maxMinutes: 240);

        var resolved = PermissionLimitResolver.Resolve(rules, policy);

        // Monthly dims picked up from policy.
        Assert.Equal(240, resolved.MaxMinutesPerMonth);
        Assert.Equal(4, resolved.MaxRequestsPerMonth);

        // Day/per-request dims have no policy fallback → still null.
        Assert.Null(resolved.MaxMinutesPerRequest);
        Assert.Null(resolved.MaxMinutesPerDay);
        Assert.Null(resolved.MaxRequestsPerDay);
    }

    [Fact] // Type-level monthly dims take precedence over policy.
    public void Resolve_type_dims_override_policy()
    {
        var rules = Rules(maxMinutesPerMonth: 120, maxRequestsPerMonth: 2);
        var policy = Policy(maxCount: 10, maxMinutes: 600);

        var resolved = PermissionLimitResolver.Resolve(rules, policy);

        Assert.Equal(120, resolved.MaxMinutesPerMonth);
        Assert.Equal(2, resolved.MaxRequestsPerMonth);
    }

    [Fact] // Null policy does not crash.
    public void Resolve_with_null_policy_is_safe()
    {
        var rules = Rules(maxMinutesPerDay: 60);
        var resolved = PermissionLimitResolver.Resolve(rules, null);

        Assert.Equal(60, resolved.MaxMinutesPerDay);
        Assert.Null(resolved.MaxMinutesPerMonth);
        Assert.Null(resolved.MaxRequestsPerMonth);
    }

    [Fact] // Behavior always comes from rules.ExceedBehavior, not from policy.PermissionCapMode.
    public void Resolve_behavior_always_from_rules()
    {
        var rules = Rules(behavior: PermissionExceedBehavior.Warn);
        var policy = Policy(mode: PermissionCapMode.Block);

        var resolved = PermissionLimitResolver.Resolve(rules, policy);

        Assert.Equal(PermissionExceedBehavior.Warn, resolved.Behavior);
    }
}
