using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Domain.Engines.Loans;
using HR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Loans.Controllers;

/// <summary>Loans &amp; salary advances created when Loan/Advance requests are approved, with their
/// installment (payroll deduction) schedules.</summary>
[Authorize]
[ApiController]
[Route("api/loans")]
public class LoansController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    public LoansController(ApplicationDbContext db, ICurrentUserService user) { _db = db; _user = user; }

    public sealed class LoanDto
    {
        public Guid Id { get; set; }
        public Guid EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public string? LoanType { get; set; }
        public string Kind { get; set; } = null!;
        public decimal Principal { get; set; }
        public int InstallmentMonths { get; set; }
        public decimal MonthlyInstallment { get; set; }
        public string Status { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public List<InstallmentDto> Installments { get; set; } = new();
    }
    public sealed class InstallmentDto { public DateTime DueMonth { get; set; } public decimal Amount { get; set; } public bool Paid { get; set; } }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<LoanDto>>>> Get([FromQuery] string? scope, [FromQuery] Guid? employeeId, CancellationToken ct)
    {
        var canSeeAll = _user.Permissions.Contains("Loans.View") || _user.Permissions.Contains("Payroll.View") || _user.Permissions.Contains("Employees.View");
        var q = _db.Loans.AsNoTracking().Include(l => l.Installments).AsQueryable();
        if (employeeId is { } eid && canSeeAll) q = q.Where(l => l.EmployeeId == eid);
        else if (scope == "all" && canSeeAll) { /* all */ }
        else
        {
            var myId = await _db.Employees.Where(e => e.UserId == _user.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            q = q.Where(l => l.EmployeeId == myId);
        }

        var loans = await q.OrderByDescending(l => l.StartDate).ToListAsync(ct);
        var empIds = loans.Select(l => l.EmployeeId).Distinct().ToList();
        var typeIds = loans.Where(l => l.LoanTypeId != null).Select(l => l.LoanTypeId!.Value).Distinct().ToList();
        var emps = await _db.Employees.Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, Name = (e.FirstNameAr ?? e.FirstName) + " " + (e.LastNameAr ?? e.LastName) }).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var types = await _db.MasterDataItems.Where(m => typeIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.NameAr, ct);

        var rows = loans.Select(l => new LoanDto
        {
            Id = l.Id, EmployeeId = l.EmployeeId,
            EmployeeName = emps.TryGetValue(l.EmployeeId, out var n) ? n : null,
            LoanType = l.LoanTypeId is { } tid && types.TryGetValue(tid, out var tn) ? tn : null,
            Kind = l.Kind, Principal = l.Principal, InstallmentMonths = l.InstallmentMonths,
            MonthlyInstallment = l.MonthlyInstallment, Status = l.Status, StartDate = l.StartDate,
            Installments = l.Installments.OrderBy(i => i.DueMonth).Select(i => new InstallmentDto { DueMonth = i.DueMonth, Amount = i.Amount, Paid = i.Paid }).ToList(),
        }).ToList();
        return Ok(ApiResponse<List<LoanDto>>.Ok(rows));
    }

    public sealed class CreateLoanRequest
    {
        public Guid EmployeeId { get; set; }
        public Guid? LoanTypeId { get; set; }
        public string? Kind { get; set; }          // Loan | Advance
        public decimal Principal { get; set; }
        public int InstallmentMonths { get; set; }
    }

    /// <summary>Manually create a loan or salary advance assigned to any employee, generating its
    /// monthly installment (deduction) schedule (HR/Finance action, no request needed).</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<LoanDto>>> Create([FromBody] CreateLoanRequest body, CancellationToken ct)
    {
        var canCreate = _user.Permissions.Contains("Loans.Approve")
            || _user.Permissions.Contains("Payroll.Create")
            || _user.Permissions.Contains("Payroll.Edit");
        if (!canCreate) return Forbid();

        if (body.EmployeeId == Guid.Empty)
            return BadRequest(ApiResponse<LoanDto>.Fail("الموظف مطلوب"));
        if (body.Principal <= 0)
            return BadRequest(ApiResponse<LoanDto>.Fail("المبلغ يجب أن يكون أكبر من صفر"));

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == body.EmployeeId, ct);
        if (emp is null)
            return BadRequest(ApiResponse<LoanDto>.Fail("الموظف غير موجود"));

        if (body.LoanTypeId is { } typeId)
        {
            var exists = await _db.MasterDataItems.AnyAsync(m => m.Id == typeId && m.ObjectType == "LoanType", ct);
            if (!exists) return BadRequest(ApiResponse<LoanDto>.Fail("نوع القرض غير صحيح"));
        }

        var kind = body.Kind == "Advance" ? "Advance" : "Loan";
        var months = Math.Max(1, body.InstallmentMonths);
        var monthly = Math.Round(body.Principal / months, 2);

        var loan = new Loan
        {
            EmployeeId = body.EmployeeId,
            LoanTypeId = body.LoanTypeId,
            Kind = kind,
            Principal = body.Principal,
            InstallmentMonths = months,
            MonthlyInstallment = monthly,
            Status = "Active",
            StartDate = DateTime.UtcNow,
        };

        var firstMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        for (var i = 0; i < months; i++)
            loan.Installments.Add(new LoanInstallment { DueMonth = firstMonth.AddMonths(i), Amount = monthly, Paid = false });

        _db.Loans.Add(loan);
        await _db.SaveChangesAsync(ct);

        var loanTypeName = loan.LoanTypeId is { } tid
            ? await _db.MasterDataItems.Where(m => m.Id == tid).Select(m => m.NameAr).FirstOrDefaultAsync(ct)
            : null;

        var dto = new LoanDto
        {
            Id = loan.Id, EmployeeId = loan.EmployeeId,
            EmployeeName = (emp.FirstNameAr ?? emp.FirstName) + " " + (emp.LastNameAr ?? emp.LastName),
            LoanType = loanTypeName,
            Kind = loan.Kind, Principal = loan.Principal, InstallmentMonths = loan.InstallmentMonths,
            MonthlyInstallment = loan.MonthlyInstallment, Status = loan.Status, StartDate = loan.StartDate,
            Installments = loan.Installments.OrderBy(i => i.DueMonth)
                .Select(i => new InstallmentDto { DueMonth = i.DueMonth, Amount = i.Amount, Paid = i.Paid }).ToList(),
        };
        return Ok(ApiResponse<LoanDto>.Ok(dto));
    }
}
