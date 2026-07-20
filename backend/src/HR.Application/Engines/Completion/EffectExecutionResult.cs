namespace HR.Application.Engines.Completion;

/// <summary>
/// What an executor returns when it did not fail. Describes the record it created/changed (for
/// completion tracking) and the before/after state (for the audit trail).
///
/// Three outcomes, not two. A blocking failure is signalled by <b>throwing</b>: the engine rolls the
/// whole completion transaction back and the request lands in CompletionFailed. A non-blocking skip
/// is signalled by <see cref="Skip"/>: the effect could not do its work, but the request is still
/// legitimately complete, so the run continues and the reason is recorded rather than swallowed.
///
/// The distinction belongs to the executor because only it knows whether its work was essential.
/// An asynchronous notification with no resolvable recipient is a skip; a custody assignment for an
/// asset somebody else already holds is a failure.
/// </summary>
public sealed class EffectExecutionResult
{
    public string? TargetEntityType { get; init; }
    public Guid? TargetRecordId { get; init; }
    public object? BeforeState { get; init; }
    public object? AfterState { get; init; }
    public string? Summary { get; init; }

    /// <summary>True when the effect deliberately did nothing. Recorded as
    /// CompletionEffectStatus.Skipped with <see cref="SkipReason"/> on the completion record.</summary>
    public bool IsSkipped { get; init; }

    /// <summary>A short, stable, greppable code — "RecipientNotResolved" — not a sentence. It is
    /// surfaced in completion history and is what an operator searches for.</summary>
    public string? SkipReason { get; init; }

    public static EffectExecutionResult Ok(
        string? targetEntityType = null,
        Guid? targetRecordId = null,
        object? before = null,
        object? after = null,
        string? summary = null)
        => new()
        {
            TargetEntityType = targetEntityType,
            TargetRecordId = targetRecordId,
            BeforeState = before,
            AfterState = after,
            Summary = summary,
        };

    /// <summary>The effect ran but had nothing to do. Does not roll back the run.</summary>
    public static EffectExecutionResult Skip(
        string reason,
        string? targetEntityType = null,
        string? summary = null)
        => new()
        {
            IsSkipped = true,
            SkipReason = reason,
            TargetEntityType = targetEntityType,
            Summary = summary ?? reason,
        };
}

/// <summary>Stable skip codes. Constants rather than free text so completion history stays
/// searchable and a UI can translate them.</summary>
public static class EffectSkipReasons
{
    public const string RecipientNotResolved = "RecipientNotResolved";
    public const string NothingToDo = "NothingToDo";
}
