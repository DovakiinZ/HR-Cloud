using FluentAssertions;
using HR.Domain.Enums;
using Xunit;

public class PayrollRunDetailsEnumsTests
{
    [Fact]
    public void Origin_System_is_default_zero()
        => ((int)PayrollTransactionOrigin.System).Should().Be(0);

    [Fact]
    public void Exclusion_reasons_are_stable_values()
    {
        ((int)PayrollExclusionReasonCode.ExcludedByScope).Should().Be(1);
        ((int)PayrollExclusionReasonCode.AlreadyInActiveRunForPeriod).Should().Be(4);
    }

    [Fact]
    public void Reserved_origins_exist()
        => System.Enum.GetNames<PayrollTransactionOrigin>()
            .Should().Contain(new[] { "RunPage", "API", "Migration", "Workflow", "ESS", "Scheduler" });
}
