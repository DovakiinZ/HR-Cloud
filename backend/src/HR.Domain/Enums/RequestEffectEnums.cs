namespace HR.Domain.Enums;

/// <summary>When a configured effect runs relative to the request's lifecycle.</summary>
public enum EffectTrigger
{
    /// <summary>The request reached its terminal approval. The default, and the only trigger the
    /// existing CompletionEngine call sites fire on today.</summary>
    FinalApproval = 1,

    Rejection = 2,
    Cancellation = 3,
}

/// <summary>
/// Whether an effect must succeed for the request to complete.
///
/// This is the transactional boundary, not a scheduling hint. <see cref="Transactional"/> effects
/// run inside CompletionEngine's transaction, so a failure rolls back every business mutation in
/// the run and the request lands in <c>CompletionFailed</c>. <see cref="Asynchronous"/> effects only
/// ever enqueue work inside that transaction — the delivery itself happens later, out of band, so a
/// mail server or a webhook endpoint being down can never undo an approved leave.
/// </summary>
public enum EffectExecutionMode
{
    Transactional = 1,
    Asynchronous = 2,

    /// <summary>Not run at approval. Enqueued as a durable completion effect and executed later by the
    /// scheduled-effect worker — on its effective date, with idempotency, retry and operator recovery.</summary>
    Deferred = 3,
}

/// <summary>Where an effect input's value comes from. Deliberately closed: the frontend chooses a
/// source and a key, never a CLR type, table, column or expression.</summary>
public enum EffectValueSource
{
    /// <summary>A field on the request's submitted form, addressed by its FormField.Code.</summary>
    FormField = 1,

    /// <summary>A property of the request itself — employeeId, requestId, requestNumber, and the
    /// leave snapshot (leaveTypeId, startDate, endDate, daysCount).</summary>
    RequestContext = 2,

    /// <summary>A literal configured by the request-type author.</summary>
    Constant = 3,

    /// <summary>The acting user at completion time — userId.</summary>
    CurrentUser = 4,

    /// <summary>The owning tenant — tenantId.</summary>
    TenantContext = 5,
}
