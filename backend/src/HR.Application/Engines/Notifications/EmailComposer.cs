using HR.Domain.Engines.Notifications;

namespace HR.Application.Engines.Notifications;

/// <summary>Pure: turns a queued row (+ optionally its attachment bytes) into an OutboundEmail.
/// If the attachment exceeds the transport cap, it is dropped and the row's Link is appended to the body
/// so the recipient still has a way to reach the file.</summary>
public static class EmailComposer
{
    public static OutboundEmail Compose(
        EmailNotificationQueue row, byte[]? attachmentBytes, string? attachmentFileName,
        string? attachmentContentType, int maxAttachmentBytes)
    {
        var body = row.Body;
        OutboundAttachment? attachment = null;

        if (attachmentBytes is { Length: > 0 } && attachmentFileName is not null && attachmentContentType is not null)
        {
            if (attachmentBytes.Length <= maxAttachmentBytes)
                attachment = new OutboundAttachment(attachmentFileName, attachmentContentType, attachmentBytes);
            else if (!string.IsNullOrWhiteSpace(row.Link))
                body = $"{body}\n{row.Link}";
        }

        return new OutboundEmail(row.ToEmail, row.Subject, body, attachment);
    }
}
