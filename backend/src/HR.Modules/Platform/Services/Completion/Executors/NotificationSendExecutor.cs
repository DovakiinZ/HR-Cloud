using HR.Application.Engines.Completion;
using HR.Domain.Engines.Notifications;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Completion.Executors;

/// <summary>
/// The asynchronous half of the effect model: this enqueues an email, it does not send one.
///
/// That distinction is the whole design. The enqueue is an ordinary insert inside the completion
/// transaction, so it rolls back with everything else if a later business effect fails — no email
/// goes out about an approval that did not happen. Delivery is then the existing
/// EmailQueueDrainer's job, running on its own hosted-service tick outside any request scope, so a
/// mail server being down retries later and can never roll back an approved leave or an attendance
/// update.
///
/// Reuses EmailNotificationQueue rather than introducing a second outbox: it already carries the
/// Category/EntityId correlation pair, retry accounting (Attempts, MaxAttempts=5) and the
/// IgnoreQueryFilters handling a background drainer needs.
/// </summary>
public sealed class NotificationSendExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NotificationSendExecutor> _log;

    public NotificationSendExecutor(ApplicationDbContext db, ILogger<NotificationSendExecutor> log)
    { _db = db; _log = log; }

    public string EffectType => EffectTypes.NotificationSend;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var subject = ctx.Str("subject") ?? $"Request {ctx.RequestNumber}";
        var body = ctx.Str("body") ?? "";

        // No explicit recipient means the requesting employee, which is the common case and saves
        // every request type from mapping it.
        var toEmail = ctx.Str("toEmail");
        if (string.IsNullOrWhiteSpace(toEmail))
            toEmail = await _db.Employees.Where(e => e.Id == ctx.EmployeeId)
                .Select(e => e.Email).FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            // Deliberately not a throw: failing the completion transaction because an employee has
            // no email on file would roll back the approval itself, which is the coupling this
            // execution mode exists to prevent.
            //
            // Equally deliberately not a silent success. Nothing is enqueued — an EmailNotificationQueue
            // row with an empty ToEmail would sit Pending, be retried five times and then be marked
            // Failed by the drainer, turning a configuration problem into recurring noise. The effect
            // is recorded as Skipped with a stable reason, visible in completion history.
            _log.LogWarning(
                "Notification.Send skipped for request {RequestNumber} ({RequestInstanceId}): {Reason}. " +
                "No 'toEmail' mapping and employee {EmployeeId} has no email address.",
                ctx.RequestNumber, ctx.RequestInstanceId, EffectSkipReasons.RecipientNotResolved, ctx.EmployeeId);

            return EffectExecutionResult.Skip(
                EffectSkipReasons.RecipientNotResolved,
                targetEntityType: "EmailNotificationQueue",
                summary: "No recipient address could be resolved; nothing was queued.");
        }

        var queued = new EmailNotificationQueue
        {
            ToEmail = toEmail,
            Subject = subject,
            Body = body,
            Category = "RequestEffect",
            EntityId = ctx.RequestInstanceId,
        };
        _db.EmailQueue.Add(queued);

        return EffectExecutionResult.Ok(
            targetEntityType: "EmailNotificationQueue",
            targetRecordId: queued.Id,
            after: new { queued.ToEmail, queued.Subject },
            summary: $"Email to {toEmail} queued for delivery");
    }
}
