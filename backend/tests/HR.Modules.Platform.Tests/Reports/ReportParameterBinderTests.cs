using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>ReportFilter.IsParameter was stored and returned but never honored — every filter's
/// saved value went into SQL regardless. These lock the substitution rules.</summary>
public class ReportParameterBinderTests
{
    private static ReportFilter Filter(bool isParameter, string? value = "5000", string? valueTo = null) => new()
    {
        FieldCode = "Salary",
        Operator = valueTo is null ? ReportFilterOperator.GreaterThan : ReportFilterOperator.Between,
        Value = value,
        ValueTo = valueTo,
        IsParameter = isParameter,
    };

    private static Dictionary<string, string?> Params(params (string Key, string? Value)[] kv)
        => kv.ToDictionary(x => x.Key, x => x.Value);

    [Fact]
    public void Supplied_value_overrides_the_stored_default_on_a_parameterized_filter()
        => ReportParameterBinder.Resolve(Filter(isParameter: true), Params(("Salary", "9000")))
            .Value.Should().Be("9000");

    [Fact]
    public void Stored_value_is_the_default_when_no_parameter_is_supplied()
        => ReportParameterBinder.Resolve(Filter(isParameter: true), Params())
            .Value.Should().Be("5000");

    [Fact]
    public void Null_parameter_dictionary_falls_back_to_the_stored_default()
        => ReportParameterBinder.Resolve(Filter(isParameter: true), null)
            .Value.Should().Be("5000");

    [Fact]
    public void A_non_parameter_filter_ignores_a_supplied_value()
    {
        // Otherwise a caller could rewrite a filter the report author fixed deliberately.
        ReportParameterBinder.Resolve(Filter(isParameter: false), Params(("Salary", "9000")))
            .Value.Should().Be("5000");
    }

    [Fact]
    public void Parameter_keys_match_the_field_code_case_insensitively()
        => ReportParameterBinder.Resolve(Filter(isParameter: true), Params(("salary", "9000")))
            .Value.Should().Be("9000");

    [Fact]
    public void Between_upper_bound_is_supplied_with_a_to_suffixed_key()
    {
        var (value, valueTo) = ReportParameterBinder.Resolve(
            Filter(isParameter: true, value: "1000", valueTo: "2000"),
            Params(("Salary", "1500"), ("Salary:to", "2500")));

        value.Should().Be("1500");
        valueTo.Should().Be("2500");
    }

    [Fact]
    public void Between_keeps_its_stored_upper_bound_when_only_the_lower_is_supplied()
    {
        var (value, valueTo) = ReportParameterBinder.Resolve(
            Filter(isParameter: true, value: "1000", valueTo: "2000"),
            Params(("Salary", "1500")));

        value.Should().Be("1500");
        valueTo.Should().Be("2000");
    }

    [Fact]
    public void An_explicitly_null_supplied_value_clears_the_filter_value()
    {
        // Distinct from "absent": the key is present, so the caller means it.
        ReportParameterBinder.Resolve(Filter(isParameter: true), Params(("Salary", null)))
            .Value.Should().BeNull();
    }
}
