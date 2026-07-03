namespace HR.Application.Engines.Finance;

/// <summary>Guards that a given (employee, effectiveDate) pair does not fall inside a payroll period
/// that is already frozen by an immutable run. Throw-on-violation; returns normally when the period
/// is open. Inject and call before persisting any create/update that carries a monetary effective date.</summary>
public interface IPayrollPeriodGuard
{
    /// <summary>Throws <see cref="PayrollPeriodClosedException"/> when an immutable run covers the
    /// period resolved from <paramref name="effectiveDate"/> for <paramref name="employeeId"/>.</summary>
    Task EnsurePeriodOpenForAsync(Guid employeeId, DateTime effectiveDate, CancellationToken ct = default);
}
