namespace HR.Application.Engines.Completion;

/// <summary>Thrown by an executor to signal a permanent failure — the worker sends the effect straight to
/// ManualReview instead of retrying (e.g. invalid configuration, a whitelist rejection).</summary>
public sealed class NonRetryableEffectException : Exception
{
    public NonRetryableEffectException(string message) : base(message) { }
}
