using HR.Application.Common.Exceptions;
using HR.Application.Engines.Audit;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.DTOs;
using HR.Modules.Employees.Entities;
using HR.Modules.Employees.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Commands;

public class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, EmployeeDto>
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditEngine _audit;

    public UpdateEmployeeCommandHandler(ApplicationDbContext context, IAuditEngine audit)
    {
        _context = context;
        _audit = audit;
    }

    public async Task<EmployeeDto> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = await _context.Employees.FindAsync(new object[] { request.Id }, cancellationToken);
        if (employee == null) throw new NotFoundException("Employee", request.Id);

        var before = new
        {
            employee.FirstName, employee.LastName, employee.Email, employee.Status,
            employee.BasicSalary, employee.DepartmentId, employee.BranchId, employee.ManagerId,
        };

        employee.FirstName = request.FirstName;
        employee.FirstNameAr = request.FirstNameAr;
        employee.LastName = request.LastName;
        employee.LastNameAr = request.LastNameAr;
        employee.Email = request.Email;
        employee.Phone = request.Phone;
        employee.Gender = request.Gender;
        employee.DateOfBirth = request.DateOfBirth;
        employee.NationalId = request.NationalId;
        employee.NationalityId = request.NationalityId;
        employee.Status = request.Status;
        employee.ContractTypeId = request.ContractTypeId;
        employee.EmploymentTypeId = request.EmploymentTypeId;
        employee.JobTitleId = request.JobTitleId;
        employee.DepartmentId = request.DepartmentId;
        employee.BranchId = request.BranchId;
        employee.ManagerId = request.ManagerId;
        employee.PhotoUrl = request.PhotoUrl;
        employee.Address = request.Address;
        employee.City = request.City;
        employee.EmergencyContactName = request.EmergencyContactName;
        employee.EmergencyContactPhone = request.EmergencyContactPhone;
        employee.BasicSalary = request.BasicSalary;
        employee.Currency = request.Currency ?? employee.Currency ?? "SAR";
        employee.GosiEnabled = request.GosiEnabled;
        employee.GosiRateOverride = request.GosiRateOverride;
        employee.PaymentMethodId = request.PaymentMethodId;
        employee.BankId = request.BankId;
        employee.BankAccountNumber = request.BankAccountNumber;
        employee.Iban = request.Iban;
        employee.SalaryCardNumber = request.SalaryCardNumber;
        employee.CardProvider = request.CardProvider;
        employee.WorkLocationId = request.WorkLocationId;
        employee.LeavePolicyId = request.LeavePolicyId;
        employee.PayrollGroupId = request.PayrollGroupId;
        employee.Notes = request.Notes;

        await SyncAllowancesAsync(employee.Id, request.Allowances, cancellationToken);
        await SyncAdditionsAsync(employee.Id, request.Additions, cancellationToken);
        await SyncDeductionsAsync(employee.Id, request.Deductions, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await _audit.LogChange("Employee", employee.Id, "Updated",
            before, new { employee.FirstName, employee.LastName, employee.Email, employee.Status,
                employee.BasicSalary, employee.DepartmentId, employee.BranchId, employee.ManagerId },
            cancellationToken);

        var result = await EmployeeProjection.MapAsync(
            _context.Employees.Where(e => e.Id == employee.Id), _context, cancellationToken);
        return result.First();
    }

    // Upsert allowances by AllowanceTypeId: update existing in place, add new, remove dropped —
    // avoids unique-constraint conflicts that a delete-then-insert would cause in one transaction.
    private async Task SyncAllowancesAsync(Guid employeeId, List<EmployeeAllowanceInput> inputs, CancellationToken ct)
    {
        var existing = await _context.EmployeeAllowances
            .Where(a => a.EmployeeId == employeeId)
            .ToListAsync(ct);

        var wanted = inputs.Where(i => i.AllowanceTypeId != Guid.Empty)
            .GroupBy(i => i.AllowanceTypeId)
            .ToDictionary(g => g.Key, g => g.Last().Amount);

        foreach (var ex in existing)
        {
            if (wanted.TryGetValue(ex.AllowanceTypeId, out var amount))
            {
                ex.Amount = amount;
                ex.IsActive = true;
            }
            else
            {
                _context.EmployeeAllowances.Remove(ex);
            }
        }

        var existingTypes = existing.Select(e => e.AllowanceTypeId).ToHashSet();
        foreach (var kv in wanted.Where(w => !existingTypes.Contains(w.Key)))
        {
            _context.EmployeeAllowances.Add(new EmployeeAllowance
            {
                EmployeeId = employeeId,
                AllowanceTypeId = kv.Key,
                Amount = kv.Value,
                IsActive = true,
            });
        }
    }

    private async Task SyncAdditionsAsync(Guid employeeId, List<EmployeeCompItemInput> inputs, CancellationToken ct)
    {
        var existing = await _context.EmployeeAdditions.Where(a => a.EmployeeId == employeeId).ToListAsync(ct);
        var wanted = inputs.Where(i => i.TypeId != Guid.Empty).GroupBy(i => i.TypeId).ToDictionary(g => g.Key, g => g.Last().Amount);
        foreach (var ex in existing)
        {
            if (wanted.TryGetValue(ex.AdditionTypeId, out var amount)) { ex.Amount = amount; ex.IsActive = true; }
            else _context.EmployeeAdditions.Remove(ex);
        }
        var have = existing.Select(e => e.AdditionTypeId).ToHashSet();
        foreach (var kv in wanted.Where(w => !have.Contains(w.Key)))
            _context.EmployeeAdditions.Add(new EmployeeAddition { EmployeeId = employeeId, AdditionTypeId = kv.Key, Amount = kv.Value, IsActive = true });
    }

    private async Task SyncDeductionsAsync(Guid employeeId, List<EmployeeCompItemInput> inputs, CancellationToken ct)
    {
        var existing = await _context.EmployeeDeductions.Where(a => a.EmployeeId == employeeId).ToListAsync(ct);
        var wanted = inputs.Where(i => i.TypeId != Guid.Empty).GroupBy(i => i.TypeId).ToDictionary(g => g.Key, g => g.Last().Amount);
        foreach (var ex in existing)
        {
            if (wanted.TryGetValue(ex.DeductionTypeId, out var amount)) { ex.Amount = amount; ex.IsActive = true; }
            else _context.EmployeeDeductions.Remove(ex);
        }
        var have = existing.Select(e => e.DeductionTypeId).ToHashSet();
        foreach (var kv in wanted.Where(w => !have.Contains(w.Key)))
            _context.EmployeeDeductions.Add(new EmployeeDeduction { EmployeeId = employeeId, DeductionTypeId = kv.Key, Amount = kv.Value, IsActive = true });
    }
}
