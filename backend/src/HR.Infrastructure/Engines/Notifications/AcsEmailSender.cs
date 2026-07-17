using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppEmailSendResult = HR.Application.Engines.Notifications.EmailSendResult;
using AppEmailOptions = HR.Application.Engines.Notifications.EmailOptions;
using AppIEmailSender = HR.Application.Engines.Notifications.IEmailSender;
using AppOutboundEmail = HR.Application.Engines.Notifications.OutboundEmail;

namespace HR.Infrastructure.Engines.Notifications;

/// <summary>Sends via Azure Communication Services Email. Never throws for a send failure.</summary>
public sealed class AcsEmailSender : AppIEmailSender
{
    private readonly EmailClient _client;
    private readonly string _sender;
    private readonly ILogger<AcsEmailSender> _logger;

    public AcsEmailSender(string connectionString, IOptions<AppEmailOptions> options, ILogger<AcsEmailSender> logger)
    {
        _client = new EmailClient(connectionString);
        _sender = options.Value.SenderAddress ?? throw new InvalidOperationException("Email:SenderAddress is required for ACS.");
        _logger = logger;
    }

    public bool CanSend => true;

    public async Task<AppEmailSendResult> SendAsync(AppOutboundEmail email, CancellationToken ct)
    {
        try
        {
            var message = new Azure.Communication.Email.EmailMessage(
                senderAddress: _sender,
                recipientAddress: email.To,
                content: new EmailContent(email.Subject) { PlainText = email.Body });

            if (email.Attachment is { } a)
                message.Attachments.Add(new Azure.Communication.Email.EmailAttachment(
                    a.FileName, a.ContentType, new BinaryData(a.Content)));

            var op = await _client.SendAsync(WaitUntil.Completed, message, ct);
            return op.HasCompleted && op.Value.Status == EmailSendStatus.Succeeded
                ? AppEmailSendResult.Ok()
                : AppEmailSendResult.Fail($"ACS status: {op.Value.Status}");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "ACS send failed for {To}.", email.To);
            return AppEmailSendResult.Fail(ex.Message);
        }
    }
}
