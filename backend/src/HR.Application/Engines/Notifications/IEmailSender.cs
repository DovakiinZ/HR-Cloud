namespace HR.Application.Engines.Notifications;

/// <summary>An email ready to hand to a transport. Names avoid clashing with the ACS SDK's EmailMessage/EmailAttachment.</summary>
public sealed record OutboundEmail(string To, string Subject, string Body, OutboundAttachment? Attachment = null);

public sealed record OutboundAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Transport outcome. A failed send is (false, error) — never an exception — so callers control retry.</summary>
public sealed record EmailSendResult(bool Sent, string? Error)
{
    public static EmailSendResult Ok() => new(true, null);
    public static EmailSendResult Fail(string error) => new(false, error);
}

/// <summary>Sends one email. Implementations MUST NOT throw for a send failure; map it to EmailSendResult.Fail.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct);
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string? SenderAddress { get; set; }
}
