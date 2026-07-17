using FluentAssertions;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class ArabicTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]     // alef hamza above → bare alef
    [InlineData("إجازة", "اجازه")]   // alef hamza below + taa marbuta → ه
    [InlineData("آمنة", "امنه")]     // alef madda → alef; taa marbuta
    [InlineData("مُوَظَّف", "موظف")]  // strip tashkeel
    [InlineData("رِيـــال", "ريال")]  // strip tatweel + tashkeel
    [InlineData("مصطفى", "مصطفي")]   // alef maqsura → ya
    [InlineData("Payroll", "payroll")] // latin lowercased, untouched otherwise
    public void Normalize_unifies_forms(string input, string expected)
        => ArabicText.Normalize(input).Should().Be(expected);

    [Fact]
    public void Normalize_null_or_empty_is_empty()
    {
        ArabicText.Normalize("").Should().Be("");
        ArabicText.Normalize(null!).Should().Be("");
    }
}
