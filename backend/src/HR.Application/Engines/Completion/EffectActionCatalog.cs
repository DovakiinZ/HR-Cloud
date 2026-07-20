using HR.Domain.Enums;

namespace HR.Application.Engines.Completion;

/// <summary>One input an effect action accepts.</summary>
/// <param name="Key">The input name the executor reads from the payload.</param>
/// <param name="LabelAr">Arabic label shown in the builder.</param>
/// <param name="LabelEn">English label.</param>
/// <param name="IsRequired">Activation fails when a required input has no mapping.</param>
/// <param name="AllowedSources">
/// Which value sources may feed this input. Closed per input: an employee id should come from the
/// request context, not from a free-text constant somebody typed.
/// </param>
public sealed record EffectInputDescriptor(
    string Key,
    string LabelAr,
    string LabelEn,
    bool IsRequired,
    IReadOnlySet<EffectValueSource> AllowedSources);

/// <summary>
/// The public contract of one action in the Effect Action Catalog: what it is called, what it does,
/// what it needs, and what the caller must be allowed to do.
///
/// This is the *only* vocabulary the request builder may use. It never exposes CLR type names, SQL,
/// table or column names — a client picks an <see cref="EffectType"/> from this catalog and maps its
/// declared inputs, and nothing else is accepted.
/// </summary>
public sealed record EffectActionDescriptor
{
    /// <summary>Catalog key, matching the executor's IEffectExecutor.EffectType.</summary>
    public required string EffectType { get; init; }

    public required string LabelAr { get; init; }
    public required string LabelEn { get; init; }
    public required string DescriptionAr { get; init; }
    public required string DescriptionEn { get; init; }

    /// <summary>Owning module, e.g. "Leave", "Attendance", "Assets". Groups the builder's picker.</summary>
    public required string Module { get; init; }

    /// <summary>Triggers this action is meaningful at. Most business effects only make sense on
    /// final approval; a notification may also fire on rejection.</summary>
    public required IReadOnlySet<EffectTrigger> SupportedTriggers { get; init; }

    /// <summary>
    /// Transactional or asynchronous. This is a property of the action, not a per-tenant choice:
    /// creating a custody record must be able to roll back an approval, and sending an email must
    /// never be able to.
    /// </summary>
    public required EffectExecutionMode ExecutionMode { get; init; }

    /// <summary>The configuration schema — the inputs a client may map.</summary>
    public required IReadOnlyList<EffectInputDescriptor> Inputs { get; init; }

    /// <summary>
    /// Permissions the caller must hold to attach this action to a request type. Attaching
    /// "Loan.Create" should require the same authority as creating loans by hand.
    /// </summary>
    public required IReadOnlyList<string> RequiredPermissions { get; init; }

    public EffectInputDescriptor? Input(string key)
        => Inputs.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The trusted registry of effect actions offered to the request builder.
///
/// Separate from IEffectExecutorRegistry on purpose. The executor registry answers "can this run?";
/// the catalog answers "may a tenant configure this, and what does it need?". An executor with no
/// descriptor stays runnable for the legacy RequestImpactMapping path but is not offered in the UI,
/// and a descriptor with no executor is caught at activation instead of at approval time.
/// </summary>
public interface IEffectActionCatalog
{
    IReadOnlyList<EffectActionDescriptor> All();
    EffectActionDescriptor? Find(string effectType);
    bool IsKnown(string effectType);
}
