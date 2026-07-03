using HR.Domain.Enums;

namespace HR.Application.Engines.Finance;

/// <summary>Request DTO for creating a PayrollTransaction from the run-page quick-action panel.
/// The service resolves the run context and stamps Origin + CreatedFromRunId automatically.</summary>
public sealed record CreateFromRunRequest(
    Guid EmployeeId,
    PayrollTransactionKind Kind,
    Guid TypeId,
    decimal Amount,
    DateTime? EffectiveDate,
    string? Notes);

/// <summary>Creates a manual PayrollTransaction scoped to an active payroll run's context.
/// Validates that the supplied EffectiveDate (or the defaulted period-end date) belongs to the
/// run's target period, then delegates to IPayrollTransactionService with Origin=RunPage.</summary>
public interface ICreateFromRunService
{
    Task<Guid> CreateAsync(Guid runId, CreateFromRunRequest req, CancellationToken ct);
}
