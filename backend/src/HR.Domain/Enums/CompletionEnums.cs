namespace HR.Domain.Enums;

/// <summary>Overall completion status of a request, tracked separately from its workflow status.</summary>
public enum CompletionRunStatus
{
    Pending = 1,
    Executing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,

    /// <summary>Inline effects finished; one or more deferred effects are still pending on the worker.</summary>
    AwaitingDeferred = 6,
}

/// <summary>Execution status of a single completion effect within a run.</summary>
public enum CompletionEffectStatus
{
    Pending = 1,
    Executing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Skipped = 6,

    /// <summary>Deferred effect waiting for its ScheduledFor date.</summary>
    Scheduled = 7,
    /// <summary>Deferred effect failed and is awaiting its next retry attempt.</summary>
    Retrying = 8,
    /// <summary>Deferred effect exhausted its retries — needs a human.</summary>
    ManualReview = 9,
}
