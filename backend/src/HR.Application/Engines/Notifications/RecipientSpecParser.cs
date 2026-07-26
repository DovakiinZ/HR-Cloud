using System.Text.Json;
using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

/// <summary>Parses and validates a rule's RecipientsJson. Strict: unknown types, deferred types,
/// wrong/missing refId, unknown properties, over-max count, and bad schema versions all fail.
/// Duplicate recipients collapse. Never throws — returns a result with errors.</summary>
public static class RecipientSpecParser
{
    private static readonly HashSet<string> EnvelopeKeys = new(StringComparer.Ordinal) { "v", "recipients" };
    private static readonly HashSet<string> RecipientKeys = new(StringComparer.Ordinal) { "type", "refId" };

    public static RecipientParseResult ParseAndValidate(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return RecipientParseResult.Fail("RecipientsJson is not valid JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return RecipientParseResult.Fail("RecipientsJson must be an object.");

            foreach (var prop in root.EnumerateObject())
                if (!EnvelopeKeys.Contains(prop.Name))
                    return RecipientParseResult.Fail($"Unknown property '{prop.Name}' on recipients envelope.");

            if (!root.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.Number
                || vEl.GetInt32() != NotificationCapabilityRegistry.CurrentSchemaVersion)
                return RecipientParseResult.Fail("Unsupported or missing recipients schema version 'v'.");

            if (!root.TryGetProperty("recipients", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return RecipientParseResult.Fail("'recipients' must be an array.");

            var errors = new List<string>();
            var specs = new List<RecipientSpec>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { errors.Add("Each recipient must be an object."); continue; }
                var hadUnknownProp = false;
                foreach (var prop in item.EnumerateObject())
                    if (!RecipientKeys.Contains(prop.Name)) { errors.Add($"Unknown property '{prop.Name}' on recipient."); hadUnknownProp = true; }
                if (hadUnknownProp) continue;

                if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String
                    || !Enum.TryParse<NotificationRecipientType>(typeEl.GetString(), ignoreCase: false, out var type))
                { errors.Add("Recipient 'type' is missing or unknown."); continue; }

                if (!NotificationCapabilityRegistry.SupportedRecipientTypes.Contains(type))
                { errors.Add($"Recipient type '{type}' is not yet supported."); continue; }

                Guid? refId = null;
                if (item.TryGetProperty("refId", out var refEl))
                {
                    if (refEl.ValueKind != JsonValueKind.String || !Guid.TryParse(refEl.GetString(), out var g))
                    { errors.Add($"Recipient '{type}' has a malformed refId."); continue; }
                    refId = g;
                }

                var needs = NotificationCapabilityRegistry.RequiresRefId(type);
                if (needs && refId is null) { errors.Add($"Recipient '{type}' requires a refId."); continue; }
                if (!needs && refId is not null) { errors.Add($"Recipient '{type}' must not carry a refId."); continue; }

                specs.Add(new RecipientSpec(type, refId));
            }

            if (specs.Count > NotificationCapabilityRegistry.MaxRecipients)
                errors.Add($"A rule may have at most {NotificationCapabilityRegistry.MaxRecipients} recipients.");

            var deduped = specs.DistinctBy(s => (s.Type, s.RefId)).ToList();

            if (errors.Count > 0) return new RecipientParseResult(null, errors);
            return new RecipientParseResult(
                new RecipientsEnvelope(NotificationCapabilityRegistry.CurrentSchemaVersion, deduped),
                Array.Empty<string>());
        }
    }

    public static string Serialize(IEnumerable<RecipientSpec> recipients)
    {
        var items = recipients.Select(r => r.RefId is { } id
            ? new { type = r.Type.ToString(), refId = id.ToString() }
            : (object)new { type = r.Type.ToString() });
        return JsonSerializer.Serialize(new { v = NotificationCapabilityRegistry.CurrentSchemaVersion, recipients = items });
    }
}
