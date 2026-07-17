using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class EmailComposerTests
{
    private static EmailNotificationQueue Row(string? link = null) => new()
        { ToEmail = "a@b.com", Subject = "Report", Body = "Body text", Link = link };

    [Fact]
    public void Compose_without_attachment_passes_body_through()
    {
        var e = EmailComposer.Compose(Row(), null, null, null, 10_000);
        e.To.Should().Be("a@b.com");
        e.Subject.Should().Be("Report");
        e.Body.Should().Be("Body text");
        e.Attachment.Should().BeNull();
    }

    [Fact]
    public void Compose_attaches_when_bytes_within_limit()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var e = EmailComposer.Compose(Row(), bytes, "r.pdf", "application/pdf", 10_000);
        e.Attachment.Should().NotBeNull();
        e.Attachment!.FileName.Should().Be("r.pdf");
        e.Attachment.ContentType.Should().Be("application/pdf");
        e.Attachment.Content.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public void Compose_oversized_attachment_is_dropped_and_link_appended()
    {
        var big = new byte[20];
        var e = EmailComposer.Compose(Row(link: "/api/files/123"), big, "r.pdf", "application/pdf", 10);
        e.Attachment.Should().BeNull();
        e.Body.Should().Contain("/api/files/123");
    }

    [Fact]
    public void Compose_oversized_attachment_with_null_link_drops_attachment_and_leaves_body_unchanged()
    {
        var big = new byte[20];
        // Link is null — the EmailComposer must not append anything to the body.
        var e = EmailComposer.Compose(Row(link: null), big, "r.pdf", "application/pdf", 10);
        e.Attachment.Should().BeNull();
        e.Body.Should().Be("Body text");
    }
}
