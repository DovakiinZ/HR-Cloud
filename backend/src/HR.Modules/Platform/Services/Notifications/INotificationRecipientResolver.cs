using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Requests;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>Resolves a single recipient spec to concrete application user ids. Returns an empty list
/// when nothing resolves (e.g. an employee with no manager) — the dispatcher logs and skips that
/// recipient. Never falls back to another person.</summary>
public interface INotificationRecipientResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(
        RecipientSpec spec, RequestInstance instance, RequestApproval? currentStep, CancellationToken ct);
}
