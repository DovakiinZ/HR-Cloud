using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Application.Engines.Documents;
using HR.Domain.Engines.Expenses;
using HR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Expenses.Controllers;

/// <summary>Expense records created when Expense Claim requests are approved.</summary>
[Authorize]
[ApiController]
[Route("api/expenses")]
public class ExpensesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IExpenseDocumentService _docs;
    public ExpensesController(ApplicationDbContext db, ICurrentUserService user, IExpenseDocumentService docs)
    { _db = db; _user = user; _docs = docs; }

    private bool CanView() => _user.Permissions.Contains("Expenses.View") || _user.Permissions.Contains("Payroll.View") || _user.Permissions.Contains("Employees.View");
    private bool CanManage() => _user.Permissions.Contains("Expenses.Approve") || _user.Permissions.Contains("Payroll.Create") || _user.Permissions.Contains("Payroll.Edit");

    public sealed class ExpenseDto
    {
        public Guid Id { get; set; }
        public Guid EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public string? Category { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SAR";
        public string? Description { get; set; }
        public string? ReceiptUrl { get; set; }
        public string Status { get; set; } = null!;
        public DateTime DecidedAt { get; set; }
        public bool IncludeInPayroll { get; set; }
        public DateTime? PayrollMonth { get; set; }
    }

    /// <summary>List expenses. scope=all (with permission) → everyone; otherwise the caller's own.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ExpenseDto>>>> Get([FromQuery] string? scope, [FromQuery] Guid? employeeId, CancellationToken ct)
    {
        var canSeeAll = _user.Permissions.Contains("Expenses.View") || _user.Permissions.Contains("Payroll.View") || _user.Permissions.Contains("Employees.View");
        var q = _db.Expenses.AsNoTracking().AsQueryable();
        if (employeeId is { } eid && canSeeAll) q = q.Where(e => e.EmployeeId == eid);
        else if (scope == "all" && canSeeAll) { /* all */ }
        else
        {
            var myId = await _db.Employees.Where(e => e.UserId == _user.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            q = q.Where(e => e.EmployeeId == myId);
        }

        var rows = await (from x in q
                          join emp in _db.Employees on x.EmployeeId equals emp.Id into ej
                          from emp in ej.DefaultIfEmpty()
                          join cat in _db.MasterDataItems on x.ExpenseCategoryId equals cat.Id into cj
                          from cat in cj.DefaultIfEmpty()
                          orderby x.DecidedAt descending
                          select new ExpenseDto
                          {
                              Id = x.Id, EmployeeId = x.EmployeeId,
                              EmployeeName = emp != null ? ((emp.FirstNameAr ?? emp.FirstName) + " " + (emp.LastNameAr ?? emp.LastName)) : null,
                              Category = cat != null ? cat.NameAr : null,
                              Amount = x.Amount, Currency = x.Currency, Description = x.Description,
                              ReceiptUrl = x.ReceiptUrl, Status = x.Status, DecidedAt = x.DecidedAt,
                              IncludeInPayroll = x.IncludeInPayroll, PayrollMonth = x.PayrollMonth,
                          }).ToListAsync(ct);
        return Ok(ApiResponse<List<ExpenseDto>>.Ok(rows));
    }

    public sealed class CreateExpenseRequest
    {
        public Guid EmployeeId { get; set; }
        public Guid? ExpenseCategoryId { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? Description { get; set; }
        public string? ReceiptUrl { get; set; }
        public string? Status { get; set; }
        public bool IncludeInPayroll { get; set; }
        public DateTime? PayrollMonth { get; set; }
    }

    /// <summary>Manually create an expense assigned to any employee (HR/Finance action, no request needed).</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Create([FromBody] CreateExpenseRequest body, CancellationToken ct)
    {
        var canCreate = _user.Permissions.Contains("Expenses.Approve")
            || _user.Permissions.Contains("Payroll.Create")
            || _user.Permissions.Contains("Payroll.Edit");
        if (!canCreate) return Forbid();

        if (body.EmployeeId == Guid.Empty)
            return BadRequest(ApiResponse<ExpenseDto>.Fail("الموظف مطلوب"));
        if (body.Amount <= 0)
            return BadRequest(ApiResponse<ExpenseDto>.Fail("المبلغ يجب أن يكون أكبر من صفر"));

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == body.EmployeeId, ct);
        if (emp is null)
            return BadRequest(ApiResponse<ExpenseDto>.Fail("الموظف غير موجود"));

        if (body.ExpenseCategoryId is { } catId)
        {
            var exists = await _db.MasterDataItems.AnyAsync(m => m.Id == catId && m.ObjectType == "ExpenseCategory", ct);
            if (!exists) return BadRequest(ApiResponse<ExpenseDto>.Fail("فئة المصروف غير صحيحة"));
        }

        // When flagged for payroll the target month is required; normalize it to the 1st of the month (UTC).
        DateTime? payrollMonth = null;
        if (body.IncludeInPayroll)
        {
            if (body.PayrollMonth is not { } pm)
                return BadRequest(ApiResponse<ExpenseDto>.Fail("يجب تحديد شهر الرواتب عند التضمين في الرواتب"));
            payrollMonth = new DateTime(pm.Year, pm.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        var status = string.IsNullOrWhiteSpace(body.Status) ? "Approved" : body.Status!.Trim();
        var expense = new Expense
        {
            EmployeeId = body.EmployeeId,
            ExpenseCategoryId = body.ExpenseCategoryId,
            Amount = body.Amount,
            Currency = string.IsNullOrWhiteSpace(body.Currency) ? "SAR" : body.Currency!.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description!.Trim(),
            ReceiptUrl = string.IsNullOrWhiteSpace(body.ReceiptUrl) ? null : body.ReceiptUrl!.Trim(),
            Status = status,
            DecidedAt = DateTime.UtcNow,
            IncludeInPayroll = body.IncludeInPayroll,
            PayrollMonth = payrollMonth,
        };
        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        var categoryName = expense.ExpenseCategoryId is { } cid
            ? await _db.MasterDataItems.Where(m => m.Id == cid).Select(m => m.NameAr).FirstOrDefaultAsync(ct)
            : null;

        var dto = new ExpenseDto
        {
            Id = expense.Id, EmployeeId = expense.EmployeeId,
            EmployeeName = (emp.FirstNameAr ?? emp.FirstName) + " " + (emp.LastNameAr ?? emp.LastName),
            Category = categoryName,
            Amount = expense.Amount, Currency = expense.Currency, Description = expense.Description,
            ReceiptUrl = expense.ReceiptUrl, Status = expense.Status, DecidedAt = expense.DecidedAt,
            IncludeInPayroll = expense.IncludeInPayroll, PayrollMonth = expense.PayrollMonth,
        };
        return Ok(ApiResponse<ExpenseDto>.Ok(dto));
    }

    /// <summary>Printable expense document (branded PDF, opened inline).</summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        if (!CanView()) return Forbid();
        if (!await _db.Expenses.AnyAsync(e => e.Id == id, ct)) return NotFound();
        var doc = await _docs.RenderAsync(id, ct);
        return File(doc.Pdf, "application/pdf");
    }

    /// <summary>Download the expense document as an attachment.</summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        if (!CanView()) return Forbid();
        if (!await _db.Expenses.AnyAsync(e => e.Id == id, ct)) return NotFound();
        var doc = await _docs.RenderAsync(id, ct);
        return File(doc.Pdf, "application/pdf", doc.FileName);
    }

    /// <summary>Cancel an expense — removes it from payroll inclusion.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Cancel(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var exp = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exp is null) return NotFound(ApiResponse<ExpenseDto>.Fail("المصروف غير موجود"));
        exp.Status = "Cancelled";
        exp.IncludeInPayroll = false;
        await _db.SaveChangesAsync(ct);

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == exp.EmployeeId, ct);
        var categoryName = exp.ExpenseCategoryId is { } cid
            ? await _db.MasterDataItems.AsNoTracking().Where(m => m.Id == cid).Select(m => m.NameAr).FirstOrDefaultAsync(ct)
            : null;
        var dto = new ExpenseDto
        {
            Id = exp.Id, EmployeeId = exp.EmployeeId,
            EmployeeName = emp != null ? (emp.FirstNameAr ?? emp.FirstName) + " " + (emp.LastNameAr ?? emp.LastName) : null,
            Category = categoryName,
            Amount = exp.Amount, Currency = exp.Currency, Description = exp.Description,
            ReceiptUrl = exp.ReceiptUrl, Status = exp.Status, DecidedAt = exp.DecidedAt,
            IncludeInPayroll = exp.IncludeInPayroll, PayrollMonth = exp.PayrollMonth,
        };
        return Ok(ApiResponse<ExpenseDto>.Ok(dto));
    }
}
