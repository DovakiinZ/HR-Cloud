using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

/// <summary>Pure: applies a send result to a queue row (Sent, or Attempts++ → Pending/Failed at cap).</summary>
public static class EmailDeliveryDecision
{
    public static void Apply(EmailNotificationQueue row, EmailSendResult result, int maxAttempts, DateTime nowUtc)
    {
        if (result.Sent)
        {
            row.Status = EmailQueueStatus.Sent;
            row.SentAt = nowUtc;
            row.Error = null;
            return;
        }

        row.Attempts += 1;
        row.Error = result.Error;
        row.Status = row.Attempts >= maxAttempts ? EmailQueueStatus.Failed : EmailQueueStatus.Pending;
    }
}
