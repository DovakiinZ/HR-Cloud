namespace HR.Application.Engines.Finance;

public sealed record AmendResult(Guid VoidedRunId, Guid NewRunId, string NewRunNumber);

/// <summary>Void / amend / reissue for posted payroll runs (SP6), on ADR PAY-4 (Reversal-over-Reopen): a
/// run is never reopened. Void reverses its ledger postings; Amend voids the old run and creates a linked
/// fresh run (the ledger nets −old + new); Reissue regenerates the run's payslips.</summary>
public interface IPayrollRunAmendmentService
{
    /// <summary>Reverse a posted run's ledger entries, flip its consumed transactions to Reversed, and move
    /// it to the terminal Voided state (releasing its hold on the period).</summary>
    Task VoidAsync(Guid runId, string reason, CancellationToken ct = default);

    /// <summary>Void the run and create a new Draft run that supersedes it (amendment chain). The new run
    /// then follows the normal lifecycle and posts fresh amounts.</summary>
    Task<AmendResult> AmendAsync(Guid runId, string reason, CancellationToken ct = default);

    /// <summary>Regenerate and re-archive the run's payslips (reuses the payslip engine). Returns the count.</summary>
    Task<int> ReissueAsync(Guid runId, CancellationToken ct = default);
}
