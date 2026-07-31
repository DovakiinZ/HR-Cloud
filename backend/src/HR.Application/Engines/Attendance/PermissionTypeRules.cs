using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Attendance;

namespace HR.Application.Engines.Attendance;

/// <summary>Typed view of an AttendancePermissionType MasterDataItem's MetadataJson (mirrors LeaveRules).
/// All limits nullable; null ⇒ fall back to AttendancePolicy, else unlimited. Eligibility null/Mode=All
/// ⇒ entire company.</summary>
public sealed class PermissionTypeRules
{
    public bool Paid { get; set; } = true;
    public int? MaxMinutesPerRequest { get; set; }
    public int? MaxMinutesPerDay { get; set; }
    public int? MaxMinutesPerMonth { get; set; }
    public int? MaxRequestsPerDay { get; set; }
    public int? MaxRequestsPerMonth { get; set; }
    public PermissionExceedBehavior ExceedBehavior { get; set; } = PermissionExceedBehavior.Block;
    public SelectionScope? Eligibility { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PermissionTypeRules Parse(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return new PermissionTypeRules();
        try { return JsonSerializer.Deserialize<PermissionTypeRules>(metadataJson, Opts) ?? new PermissionTypeRules(); }
        catch (JsonException) { return new PermissionTypeRules(); }
    }
}
