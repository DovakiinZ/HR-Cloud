using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Notifications;

/// <summary>
/// A queued email. Decoupled from request code: the Notification engine enqueues here,
/// and a sender (when SMTP is configured) drains the queue. No hardcoded SMTP in modules.
/// </summary>
public class EmailNotificationQueue : TenantEntity
{
    public Guid? UserId { get; set; }
    public string ToEmail { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? Category { get; set; }
    public Guid? EntityId { get; set; }
    public string? Link { get; set; }
    public DateTime? DueAt { get; set; }
    public EmailQueueStatus Status { get; set; } = EmailQueueStatus.Pending;
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    /// <summary>Optional StoredFile.Id to attach when delivering (e.g. a scheduled report file). FK-less, like other Guid refs.</summary>
    public Guid? AttachmentFileId { get; set; }
}
