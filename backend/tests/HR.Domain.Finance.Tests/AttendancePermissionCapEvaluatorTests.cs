using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionCapEvaluatorTests
{
    private static AttendancePolicy Policy(int? maxCount = null, int? maxMinutes = null,
        PermissionCapMode mode = PermissionCapMode.Warn)
        => new()
        {
            PermissionMaxPerMonth = maxCount,
            PermissionMaxMinutesPerMonth = maxMinutes,
            PermissionCapMode = mode,
        };

    [Fact] // No caps set (the default) never blocks, no matter how many are already approved.
    public void Unlimited_policy_always_allows()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(),
            approvedCountThisMonth: 99, approvedMinutesThisMonth: 99_999, newExcusedMinutes: 120);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // A null policy (tenant never configured one) is treated as unlimited.
    public void Null_policy_allows()
        => Assert.Equal(AttendancePermissionCapOutcome.Allowed,
            AttendancePermissionCap.Evaluate(null, 10, 10_000, 60).Outcome);

    [Fact] // cap=4, three already used → this 4th one is still within the cap.
    public void The_nth_within_count_cap_is_allowed()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(maxCount: 4), 3, 0, 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // cap=4, four already used → the 5th breaches; Block mode rejects with a reason.
    public void Over_count_cap_blocks_in_block_mode()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(maxCount: 4, mode: PermissionCapMode.Block), 4, 0, 60);
        Assert.True(d.IsBlocked);
        Assert.False(string.IsNullOrWhiteSpace(d.ReasonEn));
        Assert.False(string.IsNullOrWhiteSpace(d.ReasonAr));
    }

    [Fact] // Same breach under Warn mode is a warning, not a block.
    public void Over_count_cap_warns_in_warn_mode()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(maxCount: 4, mode: PermissionCapMode.Warn), 4, 0, 60);
        Assert.Equal(AttendancePermissionCapOutcome.Warn, d.Outcome);
        Assert.False(d.IsBlocked);
    }

    [Fact] // Minutes cap: 240 already + 120 new = 360 > 300 → breach.
    public void Over_minutes_cap_is_enforced()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(maxMinutes: 300, mode: PermissionCapMode.Block), 2, 240, 120);
        Assert.True(d.IsBlocked);
    }

    [Fact] // Landing exactly on the minutes cap is allowed (cap is a ceiling, not an exclusive bound).
    public void Exactly_at_minutes_cap_allows()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(maxMinutes: 300), 2, 240, 60);
        Assert.Equal(AttendancePermissionCapOutcome.Allowed, d.Outcome);
    }

    [Fact] // Either cap breaching is enough — count is fine but minutes overflow.
    public void Minutes_cap_alone_can_breach_while_count_is_fine()
    {
        var d = AttendancePermissionCap.Evaluate(Policy(maxCount: 10, maxMinutes: 300, mode: PermissionCapMode.Block), 1, 250, 120);
        Assert.True(d.IsBlocked);
    }
}
