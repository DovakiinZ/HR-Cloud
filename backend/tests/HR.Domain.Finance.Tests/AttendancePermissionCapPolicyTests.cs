using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionCapPolicyTests
{
    [Fact]
    public void Policy_defaults_to_unlimited_warn()
    {
        var p = new AttendancePolicy();
        Assert.Null(p.PermissionMaxPerMonth);
        Assert.Null(p.PermissionMaxMinutesPerMonth);
        Assert.Equal(PermissionCapMode.Warn, p.PermissionCapMode);
    }
}
