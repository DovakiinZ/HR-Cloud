using HR.Domain.Enums;

namespace HR.Domain.Engines.Finance.StateMachine;

/// <summary>Raised when an illegal payroll-run state transition is attempted.</summary>
public sealed class InvalidStateTransitionException : Exception
{
    public PayrollRunState From { get; }
    public PayrollRunState To { get; }

    public InvalidStateTransitionException(PayrollRunState from, PayrollRunState to)
        : base($"Illegal payroll run transition: {from} → {to}.")
    {
        From = from;
        To = to;
    }
}

/// <summary>The single source of truth for the payroll run lifecycle. State is an explicit enum (never a
/// bag of boolean flags) and every transition is validated here. Terminal states (Archived, Cancelled)
/// admit no further transitions; Completed → Locked → Archived freezes the run for good.</summary>
public static class PayrollRunStateMachine
{
    private static readonly IReadOnlyDictionary<PayrollRunState, PayrollRunState[]> Allowed =
        new Dictionary<PayrollRunState, PayrollRunState[]>
        {
            [PayrollRunState.Draft] = new[] { PayrollRunState.Preview, PayrollRunState.Cancelled },
            [PayrollRunState.Preview] = new[] { PayrollRunState.Validated, PayrollRunState.Draft, PayrollRunState.Cancelled },
            [PayrollRunState.Validated] = new[] { PayrollRunState.PendingApproval, PayrollRunState.Preview, PayrollRunState.Draft, PayrollRunState.Cancelled },
            [PayrollRunState.PendingApproval] = new[] { PayrollRunState.Approved, PayrollRunState.Preview, PayrollRunState.Draft, PayrollRunState.Cancelled },
            [PayrollRunState.Approved] = new[] { PayrollRunState.Executing, PayrollRunState.Cancelled },
            [PayrollRunState.Executing] = new[] { PayrollRunState.Completed, PayrollRunState.Failed },
            [PayrollRunState.Failed] = new[] { PayrollRunState.Executing, PayrollRunState.Cancelled },
            // Completed/Locked are posted; SP6 lets them be Voided (ledger reversed) — never reopened.
            [PayrollRunState.Completed] = new[] { PayrollRunState.Locked, PayrollRunState.Voided },
            [PayrollRunState.Locked] = new[] { PayrollRunState.Archived, PayrollRunState.Voided },
            [PayrollRunState.Archived] = Array.Empty<PayrollRunState>(),
            [PayrollRunState.Cancelled] = Array.Empty<PayrollRunState>(),
            [PayrollRunState.Voided] = Array.Empty<PayrollRunState>(),
        };

    public static IReadOnlyList<PayrollRunState> NextStates(PayrollRunState from) =>
        Allowed.TryGetValue(from, out var next) ? next : Array.Empty<PayrollRunState>();

    public static bool CanTransition(PayrollRunState from, PayrollRunState to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    /// <summary>Throws <see cref="InvalidStateTransitionException"/> if the transition is not permitted.</summary>
    public static void EnsureCanTransition(PayrollRunState from, PayrollRunState to)
    {
        if (!CanTransition(from, to)) throw new InvalidStateTransitionException(from, to);
    }

    /// <summary>True once the run is frozen for financial purposes (approved or later).</summary>
    public static bool IsImmutable(PayrollRunState state) => state is
        PayrollRunState.Approved or PayrollRunState.Executing or PayrollRunState.Completed
        or PayrollRunState.Locked or PayrollRunState.Archived;

    public static bool IsTerminal(PayrollRunState state) =>
        state is PayrollRunState.Archived or PayrollRunState.Cancelled or PayrollRunState.Voided;
}
