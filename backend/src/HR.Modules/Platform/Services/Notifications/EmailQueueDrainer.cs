using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>Drains EmailNotificationQueue: pull due rows across tenants, compose (attach StoredFile if any),
/// send via IEmailSender, apply the pure delivery decision, persist. One row failing never blocks the batch.</summary>
public sealed class EmailQueueDrainer : IEmailQueueDrainer
{
    private const int BatchSize = 25;
    private const int MaxAttempts = 5;
    private const int MaxAttachmentBytes = 10 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailQueueDrainer> _logger;

    public EmailQueueDrainer(ApplicationDbContext db, IEmailSender sender, ILogger<EmailQueueDrainer> logger)
    { _db = db; _sender = sender; _logger = logger; }

    public async Task<int> DrainAsync(CancellationToken ct)
    {
        // No transport configured — leave every Pending row untouched until a sender is provisioned.
        if (!_sender.CanSend) return 0;

        // EmailQueue is a TenantEntity (global filter) — IgnoreQueryFilters to drain every tenant.
        var batch = await _db.EmailQueue.IgnoreQueryFilters()
            .Where(e => e.Status == EmailQueueStatus.Pending && e.Attempts < MaxAttempts)
            .OrderBy(e => e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in batch)
        {
            var now = DateTime.UtcNow;
            try
            {
                byte[]? bytes = null; string? name = null; string? contentType = null;
                if (row.AttachmentFileId is { } fileId)
                {
                    var file = await _db.Files.IgnoreQueryFilters()
                        .Where(f => f.Id == fileId)
                        .Select(f => new { f.Data, f.FileName, f.ContentType })
                        .FirstOrDefaultAsync(ct);
                    if (file is not null) { bytes = file.Data; name = file.FileName; contentType = file.ContentType; }
                }

                var email = EmailComposer.Compose(row, bytes, name, contentType, MaxAttachmentBytes);
                var result = await _sender.SendAsync(email, ct);
                EmailDeliveryDecision.Apply(row, result, MaxAttempts, now);
                if (result.Sent) sent++;
            }
            catch (Exception ex)
            {
                // Defensive: senders shouldn't throw, but never let one row abort the batch.
                _logger.LogError(ex, "Email {EmailId} threw during send.", row.Id);
                EmailDeliveryDecision.Apply(row, EmailSendResult.Fail(ex.Message), MaxAttempts, now);
            }
        }

        if (batch.Count > 0) await _db.SaveChangesAsync(ct);
        return sent;
    }
}
