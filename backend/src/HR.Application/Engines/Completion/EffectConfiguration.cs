using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Domain.Enums;

namespace HR.Application.Engines.Completion;

/// <summary>
/// Where one effect input gets its value. The pair (source, key) is the entire vocabulary a client
/// may express — there is deliberately no expression language, no column name, no entity name.
/// </summary>
public sealed class EffectValueMapping
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EffectValueSource Source { get; set; }

    /// <summary>
    /// Meaning depends on Source: a FormField.Code, a RequestContext key, the literal value for
    /// Constant, or the property for CurrentUser / TenantContext.
    /// </summary>
    public string Key { get; set; } = "";
}

/// <summary>
/// A parsed <c>RequestEffectDefinition.ConfigurationJson</c>: inputKey → mapping.
/// </summary>
public sealed class EffectConfiguration
{
    public Dictionary<string, EffectValueMapping> Inputs { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Parses stored configuration. Returns null when the JSON is malformed rather than throwing:
    /// callers are either validating (and want to report it) or building intents (where a throw
    /// would fail an already-approved request over a configuration problem).
    /// </summary>
    public static EffectConfiguration? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EffectConfiguration();
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, EffectValueMapping>>(json, Options);
            if (map is null) return new EffectConfiguration();
            return new EffectConfiguration
            {
                Inputs = new Dictionary<string, EffectValueMapping>(map, StringComparer.OrdinalIgnoreCase),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(IDictionary<string, EffectValueMapping> inputs)
        => JsonSerializer.Serialize(inputs, Options);
}

/// <summary>
/// Keys addressable via <see cref="EffectValueSource.RequestContext"/>. Closed set, so a descriptor
/// can advertise exactly what is bindable and validation can reject anything else.
/// </summary>
public static class RequestContextKeys
{
    public const string EmployeeId = "employeeId";
    public const string RequestId = "requestId";
    public const string RequestNumber = "requestNumber";
    public const string RequestTypeCode = "requestTypeCode";
    public const string LeaveTypeId = "leaveTypeId";
    public const string StartDate = "startDate";
    public const string EndDate = "endDate";
    public const string DaysCount = "daysCount";
    public const string ManagerUserId = "managerUserId";
    public const string ManagerEmail = "managerEmail";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EmployeeId, RequestId, RequestNumber, RequestTypeCode, LeaveTypeId, StartDate, EndDate, DaysCount,
        ManagerUserId, ManagerEmail,
    };
}

/// <summary>Keys addressable via <see cref="EffectValueSource.CurrentUser"/>.</summary>
public static class CurrentUserKeys
{
    public const string UserId = "userId";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserId };
}

/// <summary>Keys addressable via <see cref="EffectValueSource.TenantContext"/>.</summary>
public static class TenantContextKeys
{
    public const string TenantId = "tenantId";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TenantId };
}
