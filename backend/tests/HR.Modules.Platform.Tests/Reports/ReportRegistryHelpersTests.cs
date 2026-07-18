using FluentAssertions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportRegistryHelpersTests
{
    // ── PickDisplayColumn ──────────────────────────────────────────────────────

    [Fact]
    public void PickDisplayColumn_prefers_NameAr_when_present()
        => ReportRegistryHelpers.PickDisplayColumn(new[] { "Id", "NameAr", "Code" })
            .Should().Be("NameAr");

    [Fact]
    public void PickDisplayColumn_prefers_Name_over_NameEn()
        => ReportRegistryHelpers.PickDisplayColumn(new[] { "NameEn", "Name", "Code" })
            .Should().Be("Name");

    [Fact]
    public void PickDisplayColumn_falls_back_to_first_element_when_no_priority_match()
        => ReportRegistryHelpers.PickDisplayColumn(new[] { "SomethingUnknown", "AlsoUnknown" })
            .Should().Be("SomethingUnknown");

    [Fact]
    public void PickDisplayColumn_returns_null_for_empty_collection()
        => ReportRegistryHelpers.PickDisplayColumn(Array.Empty<string>())
            .Should().BeNull();

    [Fact]
    public void PickDisplayColumn_is_case_insensitive()
        => ReportRegistryHelpers.PickDisplayColumn(new[] { "namear" })
            .Should().Be("namear");

    // ── OperatorsFor ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Number")]
    [InlineData("Decimal")]
    [InlineData("Currency")]
    [InlineData("Percentage")]
    public void OperatorsFor_numeric_type_contains_Between_and_not_Contains(string dataType)
    {
        var ops = ReportRegistryHelpers.OperatorsFor(dataType);
        ops.Should().Contain("Between");
        ops.Should().NotContain("Contains");
    }

    [Theory]
    [InlineData("Date")]
    [InlineData("DateTime")]
    public void OperatorsFor_date_type_contains_Between_and_GreaterThan(string dataType)
    {
        var ops = ReportRegistryHelpers.OperatorsFor(dataType);
        ops.Should().Contain("Between");
        ops.Should().Contain("GreaterThan");
        ops.Should().NotContain("Contains");
    }

    [Fact]
    public void OperatorsFor_Boolean_has_only_Equals()
    {
        var ops = ReportRegistryHelpers.OperatorsFor("Boolean");
        ops.Should().BeEquivalentTo(new[] { "Equals" });
    }

    [Theory]
    [InlineData("Reference")]
    [InlineData("Enum")]
    public void OperatorsFor_reference_and_enum_contain_In_and_not_Contains(string dataType)
    {
        var ops = ReportRegistryHelpers.OperatorsFor(dataType);
        ops.Should().Contain("In");
        ops.Should().NotContain("Contains");
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Guid")]
    [InlineData("SomethingElse")]
    public void OperatorsFor_text_type_contains_Contains(string dataType)
    {
        var ops = ReportRegistryHelpers.OperatorsFor(dataType);
        ops.Should().Contain("Contains");
        ops.Should().Contain("StartsWith");
        ops.Should().Contain("EndsWith");
    }
}
