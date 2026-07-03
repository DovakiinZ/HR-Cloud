namespace HR.Application.Common.Exceptions;

/// <summary>
/// A business-rule violation that is the caller's fault but is not a field-validation,
/// not-found, forbidden, or conflict error (e.g. "type is inactive", "amount must be
/// non-negative"). Surfaces as HTTP 422 with the message shown to the user.
/// Prefer this over <see cref="InvalidOperationException"/> for new domain checks so the
/// reason reaches the client instead of being swallowed as a generic 500.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Optional machine-readable code (e.g. PAYROLL_RUN_STALE) the middleware surfaces on the
    /// response envelope so clients can branch on it without parsing the human message.</summary>
    public string? Code { get; }

    public DomainException(string message) : base(message) { }

    public DomainException(string message, string? code) : base(message) => Code = code;
}
