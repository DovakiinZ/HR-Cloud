namespace HR.Application.Engines.Finance;

/// <summary>Derived (no persistence) check of whether a run's payslip snapshot still matches its
/// consumable transaction set. Returns true when either:
/// 1. An Approved, in-period, in-population transaction is NOT reflected in the current snapshot, or
/// 2. A TXN: component in the snapshot belongs to a transaction that is no longer consumable (e.g.
///    it has since been Reversed / Cancelled and dropped from the consumable set).
/// Used by Task 8 to gate Validate / Submit / Approve operations.</summary>
public interface IPayrollRunStalenessEvaluator
{
    Task<bool> IsStaleAsync(Guid runId, CancellationToken ct = default);
}
