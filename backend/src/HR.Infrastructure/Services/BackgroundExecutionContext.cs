using HR.Application.Common.Interfaces;

namespace HR.Infrastructure.Services;

/// <summary>AsyncLocal-backed ambient context. Registered as a singleton: the AsyncLocal holds the
/// per-logical-call value, so concurrent jobs (and the parallel workers inside one run) each see their
/// own tenant scope. Scopes nest and restore on dispose.</summary>
public sealed class BackgroundExecutionContext : IBackgroundExecutionContext
{
    private sealed record Scope(Guid TenantId, Guid? UserId, string? Email, Guid? CorrelationId);

    private static readonly AsyncLocal<Scope?> Current = new();

    public bool IsActive => Current.Value is not null;
    public Guid TenantId => Current.Value?.TenantId ?? Guid.Empty;
    public Guid? UserId => Current.Value?.UserId;
    public string? Email => Current.Value?.Email;
    public Guid? CorrelationId => Current.Value?.CorrelationId;

    public IDisposable Begin(Guid tenantId, Guid? userId = null, string? email = null, Guid? correlationId = null)
    {
        var previous = Current.Value;
        // A nested scope inherits the outer correlation id unless it sets its own, so provisioning
        // called from inside onboarding stays attributable to the same operation.
        Current.Value = new Scope(tenantId, userId, email, correlationId ?? previous?.CorrelationId);
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Scope? _previous;
        private bool _disposed;
        public Restore(Scope? previous) => _previous = previous;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Current.Value = _previous;
        }
    }
}
