using HR.Application.Engines.Finance.Export;
using HR.Application.Engines.Finance.Export.Bank;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Payroll.Export;

/// <summary>Builds the canonical bank payment rows for a run from its payslips + employee bank details.
/// The 2-digit Saudi bank identifier is derived from the IBAN (positions 5-6); profiles that need a
/// different bank-code source can override via their own source later.</summary>
public sealed class BankPaymentSource : IBankPaymentSource
{
    private readonly ApplicationDbContext _db;
    public BankPaymentSource(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<BankPaymentRow>> BuildAsync(Guid runId, CancellationToken ct)
    {
        var payslips = await _db.PayrollPayslips.AsNoTracking()
            .Where(p => p.PayrollRunId == runId)
            .OrderBy(p => p.EmployeeName)
            .Select(p => new { p.EmployeeId, p.EmployeeNumber, p.EmployeeName, p.NetAmount, p.Currency })
            .ToListAsync(ct);

        var ids = payslips.Select(p => p.EmployeeId).ToList();
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Iban, e.NationalId })
            .ToDictionaryAsync(e => e.Id, ct);

        return payslips.Select(p =>
        {
            emps.TryGetValue(p.EmployeeId, out var e);
            var iban = string.IsNullOrWhiteSpace(e?.Iban) ? null : e!.Iban!.Replace(" ", "");
            var bankCode = iban is { Length: >= 6 } ? iban.Substring(4, 2) : null;
            return new BankPaymentRow(p.EmployeeNumber, p.EmployeeName, iban, bankCode, e?.NationalId, p.NetAmount, p.Currency);
        }).ToList();
    }
}
