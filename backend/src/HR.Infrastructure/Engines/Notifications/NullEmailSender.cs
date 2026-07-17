using HR.Application.Engines.Notifications;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Engines.Notifications;

/// <summary>Bound when ACS is not configured: leaves rows Pending (drainer will retry once a transport exists).</summary>
public sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;
    public NullEmailSender(ILogger<NullEmailSender> logger) => _logger = logger;

    public bool CanSend => false;

    public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        _logger.LogWarning("Email transport not configured; leaving {To} pending.", email.To);
        return Task.FromResult(EmailSendResult.Fail("Email transport not configured"));
    }
}
