using HR.Domain.Enums;

namespace HR.Application.Engines.Completion;

/// <summary>
/// A resolved "intent to change" produced before execution: which effect to run, in what order,
/// and the structured JSON payload it carries. The engine materializes these into CompletionEffect
/// rows and routes each to its executor.
/// </summary>
/// <param name="EffectType">e.g. "Leave.CreateApprovedLeave".</param>
/// <param name="Sequence">1-based execution order within the request.</param>
/// <param name="Payload">JSON object string describing the effect's inputs.</param>
/// <param name="Mode">Execution boundary. Defaults to Transactional so all existing call sites are unaffected.</param>
/// <param name="ScheduledFor">UTC date/time this deferred effect should run. Null for non-deferred effects.</param>
/// <param name="MaxAttempts">How many attempts the worker makes before moving to ManualReview. Defaults to 1.</param>
public sealed record EffectIntent(
    string EffectType,
    int Sequence,
    string Payload,
    EffectExecutionMode Mode = EffectExecutionMode.Transactional,
    DateTime? ScheduledFor = null,
    int MaxAttempts = 1);
