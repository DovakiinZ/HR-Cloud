using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class RecipientSpecParserTests
{
    [Fact]
    public void Parses_valid_envelope()
    {
        var json = """{"v":1,"recipients":[{"type":"CurrentApprover"},{"type":"Role","refId":"11111111-1111-1111-1111-111111111111"}]}""";
        var r = RecipientSpecParser.ParseAndValidate(json);
        r.IsValid.Should().BeTrue();
        r.Envelope!.Recipients.Should().HaveCount(2);
        r.Envelope.Recipients[1].Type.Should().Be(NotificationRecipientType.Role);
        r.Envelope.Recipients[1].RefId.Should().NotBeNull();
    }

    [Fact]
    public void Rejects_unknown_recipient_type()
    {
        var json = """{"v":1,"recipients":[{"type":"Wizard"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_deferred_recipient_type()
    {
        var json = """{"v":1,"recipients":[{"type":"FormSelectedEmployee"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_missing_refId_when_required()
    {
        var json = """{"v":1,"recipients":[{"type":"Role"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_refId_when_forbidden()
    {
        var json = """{"v":1,"recipients":[{"type":"DirectManager","refId":"11111111-1111-1111-1111-111111111111"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_unknown_property()
    {
        var json = """{"v":1,"recipients":[{"type":"Requester","color":"red"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_over_max_recipients()
    {
        var one = """{"type":"Requester"}""";
        var many = string.Join(",", System.Linq.Enumerable.Repeat(one, 21));
        var json = $$"""{"v":1,"recipients":[{{many}}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Collapses_duplicate_recipients()
    {
        var json = """{"v":1,"recipients":[{"type":"Requester"},{"type":"Requester"}]}""";
        var r = RecipientSpecParser.ParseAndValidate(json);
        r.IsValid.Should().BeTrue();
        r.Envelope!.Recipients.Should().HaveCount(1);
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        RecipientSpecParser.ParseAndValidate("{not json").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_unsupported_schema_version()
    {
        var json = """{"v":999,"recipients":[{"type":"Requester"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }
}
