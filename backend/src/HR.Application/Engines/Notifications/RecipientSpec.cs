using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

public sealed record RecipientSpec(NotificationRecipientType Type, Guid? RefId = null);

public sealed record RecipientsEnvelope(int V, IReadOnlyList<RecipientSpec> Recipients);

public sealed record RecipientParseResult(RecipientsEnvelope? Envelope, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public static RecipientParseResult Fail(string error) => new(null, new[] { error });
}
