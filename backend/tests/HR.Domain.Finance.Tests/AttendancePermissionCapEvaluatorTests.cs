using HR.Application.Engines.Attendance;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>Ports of the old AttendancePermissionCap tests (policy monthly-dim fallback path).
/// These now exercise <see cref="PermissionLimitResolver.Resolve"/> + <see cref="AttendancePermissionCap.Evaluate"/>
/// together, keeping coverage of the AttendancePolicy monthly-dim fallback logic.</summary>
public class AttendancePermissionCapEvaluatorTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Rules with all limits null and the given behavior (so Resolve falls back to policy).</summary>
    private static PermissionTypeRules RulesNone(PermissionExceedBehavior behavior = PermissionExceedBehavior.Block)
        => new() { ExceedBehavior = behavior };

    private static AttendancePolicy Policy(int? maxCount = null, int? maxMinutes = null,
        PermissionCapMode mode = PermissionCapMode.Warn)
        => new()
        {
            PermissionMaxPerMonth = maxCount,
            PermissionMaxMinutesPerMonth = maxMinutes,
            PermissionCapMode = mode,
        };

    /// <summary>Run Resolve + Evaluate in one call (mirrors old call-site shape).</summary>
    private static AttendancePermissionCapDecision Evaluate(
        PermissionTypeRules rules, AttendancePolicy? policy,
        int approvedCountThisMonth, int approvedMinutesThisMonth, int newExcusedMinutes)
    {
        var limits = PermissionLimitResolver.Resolve(rules, policy);
        var used = new PermissionUsageTally(0, approvedMinutesThisMonth, approvedCountThisMonth, approvedCountThisMonth);
        return AttendancePermissionCap.Evaluate(limits, used, newExcusedMinutes);
    }

    // ── tests (ported from old 4-arg API) ────────────────────────────────────

    [Fact] // No caps set (the default) never blocks, no matter how many are already approved.
    public void Unlimited_policy_always_allows()
    {
        var d = Evaluate(RulesNone(), Policy(),
            approvedCountThisMonth: 99, approvedMinutesThisMonth: 99_999, newExcusedMinutes: 120);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // A null policy (tenant never configured one) is treated as unlimited.
    public void Null_policy_allows()
    {
        var d = Evaluate(RulesNone(), null, 10, 10_000, 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // cap=4, three already used → this 4th one is still within the cap.
    public void The_nth_within_count_cap_is_allowed()
    {
        // Rules has no per-type count; Resolve falls back to policy.PermissionMaxPerMonth=4.
        var d = Evaluate(RulesNone(PermissionExceedBehavior.Block), Policy(maxCount: 4),
            approvedCountThisMonth: 3, approvedMinutesThisMonth: 0, newExcusedMinutes: 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // cap=4, four already used → the 5th breaches; Block mode rejects with a reason.
    public void Over_count_cap_blocks_in_block_mode()
    {
        var d = Evaluate(RulesNone(PermissionExceedBehavior.Block), Policy(maxCount: 4, mode: PermissionCapMode.Block),
            approvedCountThisMonth: 4, approvedMinutesThisMonth: 0, newExcusedMinutes: 60);
        Assert.True(d.IsBlocked);
        Assert.False(string.IsNullOrWhiteSpace(d.ReasonEn));
        Assert.False(string.IsNullOrWhiteSpace(d.ReasonAr));
    }

    [Fact] // Same breach under Warn mode is a warning, not a block.
    public void Over_count_cap_warns_in_warn_mode()
    {
        var d = Evaluate(RulesNone(PermissionExceedBehavior.Warn), Policy(maxCount: 4, mode: PermissionCapMode.Warn),
            approvedCountThisMonth: 4, approvedMinutesThisMonth: 0, newExcusedMinutes: 60);
        Assert.Equal(AttendancePermissionCapOutcome.Warn, d.Outcome);
        Assert.False(d.IsBlocked);
    }

    [Fact] // Minutes cap: 240 already + 120 new = 360 > 300 → breach.
    public void Over_minutes_cap_is_enforced()
    {
        var d = Evaluate(RulesNone(PermissionExceedBehavior.Block), Policy(maxMinutes: 300, mode: PermissionCapMode.Block),
            approvedCountThisMonth: 2, approvedMinutesThisMonth: 240, newExcusedMinutes: 120);
        Assert.True(d.IsBlocked);
    }

    [Fact] // Landing exactly on the minutes cap is allowed (cap is a ceiling, not an exclusive bound).
    public void Exactly_at_minutes_cap_allows()
    {
        var d = Evaluate(RulesNone(), Policy(maxMinutes: 300),
            approvedCountThisMonth: 2, approvedMinutesThisMonth: 240, newExcusedMinutes: 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // Either cap breaching is enough — count is fine but minutes overflow.
    public void Minutes_cap_alone_can_breach_while_count_is_fine()
    {
        var d = Evaluate(RulesNone(PermissionExceedBehavior.Block),
            Policy(maxCount: 10, maxMinutes: 300, mode: PermissionCapMode.Block),
            approvedCountThisMonth: 1, approvedMinutesThisMonth: 250, newExcusedMinutes: 120);
        Assert.True(d.IsBlocked);
    }
}
