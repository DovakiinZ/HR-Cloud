using HR.Domain.Engines.Requests;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>The centralized "event → rule → resolver → delivery" service. Fully failure-isolated:
/// it never throws to its caller, so a notification problem can never roll back a request transition.</summary>
public interface IWorkflowNotificationDispatcher
{
    Task DispatchAsync(WorkflowNotificationEvent evt, RequestInstance instance, RequestApproval? step, CancellationToken ct);
}
