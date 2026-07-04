namespace HR.Modules.Employees.DTOs;

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string EmployeeNumber { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string? FirstNameAr { get; set; }
    public string LastName { get; set; } = null!;
    public string? LastNameAr { get; set; }
    public string FullName => $"{FirstName} {LastName}";
    public string? FullNameAr => FirstNameAr != null && LastNameAr != null ? $"{FirstNameAr} {LastNameAr}" : null;
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string Gender { get; set; } = null!;
    public string GenderAr { get; set; } = null!;
    public DateTime DateOfBirth { get; set; }
    public string? NationalId { get; set; }

    // Nationality (master data reference)
    public Guid? NationalityId { get; set; }
    public string? Nationality { get; set; }
    public string? NationalityAr { get; set; }

    public string Status { get; set; } = null!;
    public string StatusAr { get; set; } = null!;

    // Contract type (master data reference)
    public Guid? ContractTypeId { get; set; }
    public string? ContractType { get; set; }
    public string? ContractTypeAr { get; set; }

    // Employment type (master data reference)
    public Guid? EmploymentTypeId { get; set; }
    public string? EmploymentType { get; set; }
    public string? EmploymentTypeAr { get; set; }

    public DateTime HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }

    // Job title (master data reference)
    public Guid? JobTitleId { get; set; }
    public string? JobTitle { get; set; }
    public string? JobTitleAr { get; set; }

    // Organization assignment (Core entities)
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid? ManagerId { get; set; }
    public string? ManagerName { get; set; }

    // Contact / emergency
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    // Compensation & payment
    public decimal BasicSalary { get; set; }
    public string? Currency { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentMethodAr { get; set; }
    public string? PaymentMethodCode { get; set; }   // BANK_TRANSFER | CASH | SALARY_CARD — drives conditional UI/export
    public Guid? BankId { get; set; }
    public string? Bank { get; set; }
    public string? BankAr { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? Iban { get; set; }
    public string? SalaryCardNumber { get; set; }
    public string? CardProvider { get; set; }

    // Attendance & assignment (master data references)
    public Guid? WorkLocationId { get; set; }
    public string? WorkLocation { get; set; }
    public string? WorkLocationAr { get; set; }
    public Guid? LeavePolicyId { get; set; }
    public string? LeavePolicy { get; set; }
    public string? LeavePolicyAr { get; set; }
    public Guid? PayrollGroupId { get; set; }
    public string? PayrollGroup { get; set; }
    public string? PayrollGroupAr { get; set; }

    public string? PhotoUrl { get; set; }
    public string? Notes { get; set; }

    public List<EmployeeAllowanceDto> Allowances { get; set; } = new();
    public List<EmployeeCompItemDto> Additions { get; set; } = new();
    public List<EmployeeCompItemDto> Deductions { get; set; } = new();

    // Backend-computed salary breakdown (single source of truth for chart/waterfall/export).
    public decimal TotalAllowances { get; set; }
    public decimal TotalAdditions { get; set; }
    public decimal TotalDeductions { get; set; }   // excludes GOSI
    public decimal GosiRate { get; set; }
    public decimal GosiAmount { get; set; }
    public bool GosiEnabled { get; set; } = true;
    public decimal? GosiRateOverride { get; set; }
    public decimal GrossSalary { get; set; }       // basic + allowances + additions
    public decimal NetSalary { get; set; }         // gross - deductions - gosi

    public DateTime CreatedAt { get; set; }
}

public class EmployeeAllowanceDto
{
    public Guid Id { get; set; }
    public Guid AllowanceTypeId { get; set; }
    public string? AllowanceType { get; set; }
    public string? AllowanceTypeAr { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>A resolved salary addition or deduction line (type id + bilingual name + amount).</summary>
public class EmployeeCompItemDto
{
    public Guid Id { get; set; }
    public Guid TypeId { get; set; }
    public string? Type { get; set; }
    public string? TypeAr { get; set; }
    public string? Code { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
}
