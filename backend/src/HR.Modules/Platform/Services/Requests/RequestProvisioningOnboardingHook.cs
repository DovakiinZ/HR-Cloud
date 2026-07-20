using HR.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Requests;

/// <summary>
/// Provisions the built-in request types when a tenant is created, so a new account arrives with a
/// working Request Center instead of an empty one that somebody has to remember to seed by hand.
///
/// Failures are logged and swallowed. Registration has already created the tenant, the admin user
/// and their permissions by the time this runs; aborting it here would leave a half-made account
/// behind and lose the caller their credentials. Provisioning is idempotent and re-runnable from
/// the API, so the recovery for a failed hook is to call it again.
/// </summary>
public sealed class RequestProvisioningOnboardingHook : ITenantOnboardingHook
{
    private readonly IRequestProvisioningService _provisioning;
    private readonly ILogger<RequestProvisioningOnboardingHook> _log;

    public RequestProvisioningOnboardingHook(
        IRequestProvisioningService provisioning,
        ILogger<RequestProvisioningOnboardingHook> log)
    { _provisioning = provisioning; _log = log; }

    public int Order => 10;

    public async Task OnTenantCreatedAsync(Guid tenantId, Guid? actorUserId, CancellationToken ct)
    {
        try
        {
            var result = await _provisioning.ProvisionTenantAsync(tenantId, actorUserId, ct);
            _log.LogInformation(
                "Onboarding provisioned {Created} request types for tenant {TenantId} (correlation {CorrelationId}).",
                result.Created, tenantId, result.CorrelationId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Onboarding provisioning failed for tenant {TenantId}. The tenant was created successfully; " +
                "re-run POST /api/requests/provision to complete it.", tenantId);
        }
    }
}
