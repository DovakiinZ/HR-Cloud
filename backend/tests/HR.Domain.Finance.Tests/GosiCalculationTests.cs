using FluentAssertions;
using HR.Domain.Engines.Finance;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>#14 — the effective GOSI rate: the employee override when set, otherwise the tenant default,
/// and always 0 when GOSI is disabled for the employee.</summary>
public class GosiCalculationTests
{
    [Theory]
    [InlineData(true, null, 9.75, 9.75)]   // enabled, no override → tenant default
    [InlineData(true, 5.0, 9.75, 5.0)]     // enabled, override → override
    [InlineData(true, 0.0, 9.75, 0.0)]     // enabled, explicit 0% override → 0
    [InlineData(false, 5.0, 9.75, 0.0)]    // disabled → 0 regardless of override
    [InlineData(false, null, 9.75, 0.0)]   // disabled → 0
    public void EffectiveRate_resolves_override_then_default_then_zero_when_disabled(
        bool enabled, double? over, double tenantDefault, double expected)
    {
        var ov = over.HasValue ? (decimal?)(decimal)over.Value : null;
        GosiCalculation.EffectiveRate(enabled, ov, (decimal)tenantDefault).Should().Be((decimal)expected);
    }
}
