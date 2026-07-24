using FluentAssertions;
using HR.Application.Engines.Forms;
using Xunit;

namespace HR.Modules.Platform.Tests.Forms;

public class FormFieldClassificationTests
{
    [Theory]
    [InlineData("{\"classification\":\"SystemRequired\"}", FieldClassification.SystemRequired)]
    [InlineData("{\"classification\":\"BusinessRequired\"}", FieldClassification.BusinessRequired)]
    [InlineData("{\"classification\":\"Optional\"}", FieldClassification.Optional)]
    [InlineData("{\"classification\":\"Custom\"}", FieldClassification.Custom)]
    public void Parses_declared_classification(string json, FieldClassification expected)
        => FormFieldClassification.Of(json).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"classification\":\"Bogus\"}")]
    public void Defaults_to_optional_when_absent_or_invalid(string? json)
        => FormFieldClassification.Of(json).Should().Be(FieldClassification.Optional);

    [Fact]
    public void Only_system_required_is_locked()
    {
        FormFieldClassification.IsLocked(FieldClassification.SystemRequired).Should().BeTrue();
        FormFieldClassification.IsLocked(FieldClassification.BusinessRequired).Should().BeFalse();
        FormFieldClassification.IsLocked(FieldClassification.Optional).Should().BeFalse();
        FormFieldClassification.IsLocked(FieldClassification.Custom).Should().BeFalse();
    }

    [Fact]
    public void With_round_trips_through_Of()
    {
        var json = FormFieldClassification.With(FieldClassification.SystemRequired);
        FormFieldClassification.Of(json).Should().Be(FieldClassification.SystemRequired);
    }
}
