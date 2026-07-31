using HR.Application.Engines.Attendance; // PermissionTypeRules
using HR.Domain.Engines.Attendance;      // PermissionExceedBehavior
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PermissionTypeRulesTests
{
    [Fact] // Missing/empty metadata → safe defaults (paid, unlimited, Block, no eligibility filter).
    public void Parse_null_returns_paid_unlimited_block()
    {
        var r = PermissionTypeRules.Parse(null);
        Assert.True(r.Paid);
        Assert.Null(r.MaxMinutesPerMonth);
        Assert.Null(r.MaxRequestsPerDay);
        Assert.Equal(PermissionExceedBehavior.Block, r.ExceedBehavior);
        Assert.Null(r.Eligibility);
    }

    [Fact] // Round-trips the config an admin would save.
    public void Parse_reads_limits_paid_and_behavior()
    {
        var json = "{\"paid\":false,\"maxMinutesPerDay\":120,\"maxRequestsPerMonth\":4,\"exceedBehavior\":2}";
        var r = PermissionTypeRules.Parse(json);
        Assert.False(r.Paid);
        Assert.Equal(120, r.MaxMinutesPerDay);
        Assert.Equal(4, r.MaxRequestsPerMonth);
        Assert.Equal(PermissionExceedBehavior.RequireApprovalOverride, r.ExceedBehavior);
    }
}
