using System.Text.Json;

namespace HR.Application.Engines.Forms;

/// <summary>How a request form field is governed. Absent/unknown metadata is treated as Optional so
/// existing fields keep working unchanged.</summary>
public enum FieldClassification { SystemRequired, BusinessRequired, Optional, Custom }

/// <summary>Pure reader/writer for a FormField's classification, stored in FormField.MetadataJson as
/// {"classification":"...","isLocked":bool}. Total: any absent/invalid value → Optional.</summary>
public static class FormFieldClassification
{
    public static FieldClassification Of(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return FieldClassification.Optional;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("classification", out var c)
                && c.ValueKind == JsonValueKind.String
                && Enum.TryParse<FieldClassification>(c.GetString(), ignoreCase: true, out var parsed))
                return parsed;
        }
        catch (JsonException) { /* fall through to default */ }
        return FieldClassification.Optional;
    }

    public static bool IsLocked(FieldClassification c) => c == FieldClassification.SystemRequired;

    public static string With(FieldClassification c)
        => JsonSerializer.Serialize(new { classification = c.ToString(), isLocked = IsLocked(c) });
}
