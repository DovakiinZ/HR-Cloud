using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;

namespace HR.Modules.Platform.Services.Completion;

public interface IEffectPermissionGuard
{
    /// <summary>True when the caller holds every permission the action declares.</summary>
    bool CanConfigure(string effectType);

    /// <summary>Throws <see cref="ForbiddenException"/> naming the first missing permission.</summary>
    void EnsureCanConfigure(string effectType);

    /// <summary>The subset of the catalog the caller may configure — what the builder is offered.</summary>
    IReadOnlyList<EffectActionDescriptor> ConfigurableActions();
}

/// <summary>
/// Gates *configuring* a business action, separately from editing request types.
///
/// Being allowed to edit a request type is not the same as being allowed to make it create loans.
/// Without this, anyone with Platform.Workflows.Edit could attach Loan.Create to a request and
/// thereby create loans they could never create directly — a privilege escalation dressed as
/// configuration. The catalog already declares what each action needs; this enforces it.
///
/// Two deliberate scope limits:
///
///   • Configuration only, never execution. CompletionEngine does not consult this: the approver at
///     completion time is rarely the person who configured the effect, and requiring them to hold
///     Loans.Create to approve a loan request would break every approval chain.
///   • Provisioning is unaffected. It writes required effects directly through the DbContext rather
///     than through the admin services, which is what lets a tenant be provisioned during onboarding
///     — where there is no HTTP principal and therefore no permissions at all.
///
/// AND semantics, unlike RequirePermissionAttribute's OR: an action declaring two permissions needs
/// both, because they describe different capabilities rather than alternative routes to one.
/// </summary>
public sealed class EffectPermissionGuard : IEffectPermissionGuard
{
    private readonly ICurrentUserService _user;
    private readonly IEffectActionCatalog _catalog;

    public EffectPermissionGuard(ICurrentUserService user, IEffectActionCatalog catalog)
    { _user = user; _catalog = catalog; }

    public bool CanConfigure(string effectType)
    {
        var descriptor = _catalog.Find(effectType);
        // An unknown action is not a permission problem — the validator reports it as invalid input,
        // and answering "forbidden" here would mask a typo as an authorization failure.
        if (descriptor is null) return true;
        return MissingPermission(descriptor) is null;
    }

    public void EnsureCanConfigure(string effectType)
    {
        var descriptor = _catalog.Find(effectType);
        if (descriptor is null) return;

        if (MissingPermission(descriptor) is { } missing)
            throw new ForbiddenException(
                $"Configuring '{descriptor.LabelEn}' ({descriptor.EffectType}) requires the '{missing}' permission. " +
                "Editing request types does not by itself grant permission to configure this action.");
    }

    public IReadOnlyList<EffectActionDescriptor> ConfigurableActions()
        => _catalog.All().Where(d => MissingPermission(d) is null).ToList();

    private string? MissingPermission(EffectActionDescriptor descriptor)
        => descriptor.RequiredPermissions.FirstOrDefault(p => !Holds(p));

    private bool Holds(string permission)
        => _user.Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
}
