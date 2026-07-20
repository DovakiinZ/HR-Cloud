namespace HR.Application.Common.Interfaces;

/// <summary>
/// Work that runs once, when a tenant is first created.
///
/// Declared here rather than in a module because Identity owns registration but must not depend on
/// Platform — the two modules reference HR.Application and not each other. Registering a hook is how
/// a module says "a new tenant needs this from me" without Identity knowing what any of them do.
///
/// Multiple hooks may be registered; the caller resolves them all. A hook must be idempotent, since
/// provisioning can also be re-run by hand, and must not throw for anything recoverable: failing
/// tenant creation because a downstream catalogue could not be seeded trades a fixable problem for
/// an unusable account.
/// </summary>
public interface ITenantOnboardingHook
{
    /// <summary>Ordering hint; lower runs first. Hooks that others depend on should sort earlier.</summary>
    int Order => 0;

    Task OnTenantCreatedAsync(Guid tenantId, Guid? actorUserId, CancellationToken ct);
}
