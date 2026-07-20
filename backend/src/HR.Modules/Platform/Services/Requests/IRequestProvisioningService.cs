namespace HR.Modules.Platform.Services.Requests;

public enum ProvisionOutcome
{
    /// <summary>Newly created on this pass.</summary>
    Created = 1,
    /// <summary>Already present and already at the shipped SeedVersion; untouched.</summary>
    AlreadyCurrent = 2,
    /// <summary>Present at an older SeedVersion and brought forward in place.</summary>
    Upgraded = 3,
}

public sealed record ProvisionedRequest(string Code, ProvisionOutcome Outcome, int FromVersion, int ToVersion, IReadOnlyList<string> Changes);

public sealed record RequestProvisioningResult
{
    public Guid TenantId { get; init; }
    public Guid CorrelationId { get; init; }
    public int Created { get; init; }
    public int Upgraded { get; init; }
    public int AlreadyCurrent { get; init; }
    public IReadOnlyList<ProvisionedRequest> Requests { get; init; } = Array.Empty<ProvisionedRequest>();
}

/// <summary>
/// Provisions the built-in request types for one tenant, explicitly.
///
/// Separate from IRequestSeeder because they answer different questions. The seeder creates the
/// full catalogue of system requests for the *current* scope and never revisits what it wrote.
/// This owns the lifecycle: which tenant, at which SeedVersion, and what changes when a newer
/// version ships. It runs the seeder inside a tenant execution scope rather than duplicating it.
/// </summary>
public interface IRequestProvisioningService
{
    /// <summary>
    /// Bring a tenant up to the current shipped version. Idempotent, non-destructive, and safe to
    /// call on every start-up or from onboarding. Runs in its own tenant scope, so it does not
    /// require — or consult — an authenticated caller.
    /// </summary>
    Task<RequestProvisioningResult> ProvisionTenantAsync(Guid tenantId, Guid? actorUserId, CancellationToken ct);
}
