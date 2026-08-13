using HR.Application.Engines.Completion;
using HR.Modules.Platform.Services.Requests;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

public class OvertimeEffectWiringTests
{
    [Fact]
    public void Overtime_request_is_wired_to_the_overtime_addition_effect()
    {
        var specs = SystemRequestEffects.Required["OVERTIME_REQUEST"];
        var overtime = Assert.Single(specs);

        Assert.Equal(EffectTypes.OvertimeCreateAddition, overtime.EffectType);
        Assert.Equal("startDate", overtime.Inputs["date"].Key);
        Assert.Equal("hours", overtime.Inputs["hours"].Key);
        Assert.Equal("reason", overtime.Inputs["reason"].Key);
    }
}
