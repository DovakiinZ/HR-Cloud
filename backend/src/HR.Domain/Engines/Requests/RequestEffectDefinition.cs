using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Requests;

/// <summary>
/// One configured action on a request type: "when this request is finally approved, assign the
/// asset in field <c>assetId</c> to the requesting employee".
///
/// This is configuration, not history. It is authored once per request type and reused by every
/// instance. The execution record stays <see cref="Completion.CompletionEffect"/> — one row per
/// effect per run, carrying the resolved payload, status, timings and failure reason. Nothing here
/// is written during completion.
///
/// Effects are addressed by <see cref="EffectType"/>, a key from the trusted Effect Action Catalog
/// (e.g. "Assets.AssignCustody"). The catalog is the only vocabulary: a CLR type name, table, column
/// or expression is never accepted from a client.
/// </summary>
public class RequestEffectDefinition : TenantEntity
{
    public Guid RequestTypeId { get; set; }

    /// <summary>Lifecycle point this effect fires at. FinalApproval is the default and the only one
    /// the current CompletionEngine call sites reach.</summary>
    public EffectTrigger Trigger { get; set; } = EffectTrigger.FinalApproval;

    /// <summary>Catalog action key, "{Module}.{Action}". Must resolve in both the Effect Action
    /// Catalog and the EffectExecutorRegistry before the request type can be activated.</summary>
    public string EffectType { get; set; } = null!;

    /// <summary>Version of the action contract this configuration was authored against. Lets a
    /// descriptor change its schema later without silently reinterpreting existing rows.</summary>
    public int EffectVersion { get; set; } = 1;

    /// <summary>Execution order within a trigger. Ties break on Id so ordering is deterministic.</summary>
    public int Sequence { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Protected: this effect is part of what the request type *means*, and a tenant may not disable
    /// or delete it. Set by provisioning on the system requests whose behaviour other modules depend
    /// on — leave balance, attendance, payroll. An optional effect a tenant added themselves is
    /// never required.
    /// </summary>
    public bool IsRequired { get; set; }

    public EffectExecutionMode ExecutionMode { get; set; } = EffectExecutionMode.Transactional;

    /// <summary>
    /// The input mapping, as a JSON object of inputKey → {source, key}. Validated against the
    /// descriptor's schema and the linked form on activation; see EffectConfiguration.
    /// </summary>
    public string ConfigurationJson { get; set; } = "{}";

    public RequestType RequestType { get; set; } = null!;
}
