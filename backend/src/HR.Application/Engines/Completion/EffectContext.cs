using System.Globalization;
using System.Text.Json;

namespace HR.Application.Engines.Completion;

/// <summary>
/// Everything an executor needs to apply one effect, without coupling it to Request/Form internals.
/// The structured <see cref="Payload"/> (materialized at completion time) carries the intent; the
/// typed readers below pull values out of it. Executors load any entities they need from the
/// database by <see cref="EmployeeId"/>.
/// </summary>
public sealed class EffectContext
{
    public required Guid RequestInstanceId { get; init; }
    public required string RequestNumber { get; init; }
    public required string RequestTypeCode { get; init; }
    public required Guid EmployeeId { get; init; }
    public Guid? ActorUserId { get; init; }

    /// <summary>Stable per-effect key for deferred execution; executors touching external systems may use
    /// it to dedupe. Null for inline effects.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>The effect's structured intent payload (JSONB).</summary>
    public required JsonElement Payload { get; init; }

    // ── typed payload readers ───────────────────────────────────────────────────

    public string? Str(string key)
        => Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty(key, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
            : null;

    public bool Bool(string key)
        => Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty(key, out var v)
            && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    public decimal Dec(string key)
        => decimal.TryParse(Str(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public int Int(string key)
        => int.TryParse(Str(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public Guid? Guid(string key)
        => System.Guid.TryParse(Str(key), out var g) ? g : null;

    public DateTime? Date(string key)
        => DateTime.TryParse(Str(key), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;
}
