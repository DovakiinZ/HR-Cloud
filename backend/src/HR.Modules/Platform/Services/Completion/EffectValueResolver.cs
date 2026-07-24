using HR.Application.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>Everything a mapping may read from, gathered once per request instance.</summary>
public sealed class EffectResolutionContext
{
    public required RequestInstance Instance { get; init; }
    public required string RequestTypeCode { get; init; }
    public required Guid TenantId { get; init; }
    public Guid? ActorUserId { get; init; }

    /// <summary>fieldCode → (value, fileUrl) from the submitted form.</summary>
    public required IReadOnlyDictionary<string, (string? Value, string? FileUrl)> FormValues { get; init; }

    /// <summary>The requester's manager's application UserId, or null when no manager is set.</summary>
    public Guid? ManagerUserId { get; init; }

    /// <summary>The requester's manager's email, or null when no manager is set.</summary>
    public string? ManagerEmail { get; init; }
}

/// <summary>
/// Turns a configured mapping into the concrete payload an executor receives.
///
/// Resolution is total: an unmapped or unresolvable input becomes null rather than throwing.
/// Validation already refused to activate a request type with a broken mapping, so a null here
/// means the submitter left an optional field blank — which the executors already handle. Throwing
/// at this point would fail a request that a human has already approved.
/// </summary>
public static class EffectValueResolver
{
    public static Dictionary<string, object?> Resolve(EffectConfiguration config, EffectResolutionContext ctx)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (inputKey, mapping) in config.Inputs)
            payload[inputKey] = ResolveOne(mapping, ctx);
        return payload;
    }

    private static object? ResolveOne(EffectValueMapping mapping, EffectResolutionContext ctx) => mapping.Source switch
    {
        EffectValueSource.Constant => mapping.Key,

        // A file field carries its payload in FileUrl rather than Value — an attachment mapped to a
        // "receipt" input must resolve to the URL, not to an empty string.
        EffectValueSource.FormField => ctx.FormValues.TryGetValue(mapping.Key, out var v)
            ? (v.Value ?? v.FileUrl)
            : null,

        EffectValueSource.RequestContext => mapping.Key.ToLowerInvariant() switch
        {
            "employeeid" => ctx.Instance.EmployeeId,
            "requestid" => ctx.Instance.Id,
            "requestnumber" => ctx.Instance.RequestNumber,
            "requesttypecode" => ctx.RequestTypeCode,
            "leavetypeid" => ctx.Instance.LeaveTypeId,
            "startdate" => ctx.Instance.StartDate,
            "enddate" => ctx.Instance.EndDate,
            "dayscount" => ctx.Instance.DaysCount,
            "manageruserid" => ctx.ManagerUserId,
            "manageremail" => ctx.ManagerEmail,
            _ => null,
        },

        EffectValueSource.CurrentUser => mapping.Key.Equals(CurrentUserKeys.UserId, StringComparison.OrdinalIgnoreCase)
            ? ctx.ActorUserId
            : null,

        EffectValueSource.TenantContext => mapping.Key.Equals(TenantContextKeys.TenantId, StringComparison.OrdinalIgnoreCase)
            ? ctx.TenantId
            : null,

        _ => null,
    };
}
