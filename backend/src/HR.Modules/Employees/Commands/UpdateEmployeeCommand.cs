using HR.Domain.Enums;
using HR.Modules.Employees.DTOs;
using MediatR;

namespace HR.Modules.Employees.Commands;

public record UpdateEmployeeCommand : IRequest<EmployeeDto>
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = null!;
    public string? FirstNameAr { get; init; }
    public string LastName { get; init; } = null!;
    public string? LastNameAr { get; init; }
    public string Email { get; init; } = null!;
    public string? Phone { get; init; }
    public Gender Gender { get; init; }
    public DateTime DateOfBirth { get; init; }
    public string? NationalId { get; init; }
    public Guid? NationalityId { get; init; }
    public EmployeeStatus Status { get; init; }
    public Guid? ContractTypeId { get; init; }
    public Guid? EmploymentTypeId { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? BranchId { get; init; }
    public Guid? ManagerId { get; init; }
    public string? PhotoUrl { get; init; }

    // Contact / emergency
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }

    // Compensation & payment
    public decimal BasicSalary { get; init; }
    public string? Currency { get; init; }
    public bool GosiEnabled { get; init; } = true;
    public decimal? GosiRateOverride { get; init; }
    public Guid? PaymentMethodId { get; init; }
    public Guid? BankId { get; init; }
    public string? BankAccountNumber { get; init; }
    public string? Iban { get; init; }
    public string? SalaryCardNumber { get; init; }
    public string? CardProvider { get; init; }

    // Attendance & assignment
    public Guid? WorkLocationId { get; init; }
    public Guid? LeavePolicyId { get; init; }
    public Guid? PayrollGroupId { get; init; }

    public string? Notes { get; init; }

    public List<EmployeeAllowanceInput> Allowances { get; init; } = new();
    public List<EmployeeCompItemInput> Additions { get; init; } = new();
    public List<EmployeeCompItemInput> Deductions { get; init; } = new();
}
