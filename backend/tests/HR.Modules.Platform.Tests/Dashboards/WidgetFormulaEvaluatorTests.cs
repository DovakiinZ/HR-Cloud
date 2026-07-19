using System.Collections.Generic;
using FluentAssertions;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.Dashboards;

public class WidgetFormulaEvaluatorTests
{
    [Fact]
    public void Ratio_formula_over_measures()
        => WidgetFormulaEvaluator.Evaluate("m1 / m2 * 100", new Dictionary<string, double> { ["m1"] = 3, ["m2"] = 12 })
            .Should().Be(25d);

    [Fact]
    public void Round_function_is_available()
        => WidgetFormulaEvaluator.Evaluate("round(a + b, 0)", new Dictionary<string, double> { ["a"] = 1.4, ["b"] = 1.4 })
            .Should().Be(3d); // round(2.8, 0)

    [Fact]
    public void Valid_formula_returns_null_reason()
        => WidgetFormulaEvaluator.Validate("m1 / m2").Should().BeNull();

    [Fact]
    public void Invalid_formula_returns_reason()
        => WidgetFormulaEvaluator.Validate("m1 / / m2").Should().NotBeNull();
}
