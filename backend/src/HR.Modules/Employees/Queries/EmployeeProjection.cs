using System.Text.Json;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.DTOs;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Queries;

/// <summary>
/// Maps Employee entities to EmployeeDto, resolving governed master-data references
/// (JobTitle / Nationality / ContractType / EmploymentType / PaymentMethod / Bank /
/// WorkLocation / LeavePolicy / PayrollGroup) and Core org assignments (Department /
/// Branch / Manager) to display labels. Resolves all master-data refs in a single
/// dictionary lookup rather than a wide join, and loads per-employee allowances.
/// </summary>
public static class EmployeeProjection
{
    public static async Task<List<EmployeeDto>> MapAsync(
        IQueryable<Employee> source, ApplicationDbContext ctx, CancellationToken ct)
    {
        var employees = await source.AsNoTracking().ToListAsync(ct);
        if (employees.Count == 0) return new List<EmployeeDto>();

        var empIds = employees.Select(e => e.Id).ToList();

        // Per-employee allowance / addition / deduction overrides.
        var allowances = await ctx.EmployeeAllowances
            .Where(a => empIds.Contains(a.EmployeeId))
            .Select(a => new { a.Id, a.EmployeeId, a.AllowanceTypeId, a.Amount, a.IsActive })
            .ToListAsync(ct);
        var additions = await ctx.EmployeeAdditions
            .Where(a => empIds.Contains(a.EmployeeId))
            .Select(a => new { a.Id, a.EmployeeId, TypeId = a.AdditionTypeId, a.Amount, a.IsActive })
            .ToListAsync(ct);
        var deductions = await ctx.EmployeeDeductions
            .Where(a => empIds.Contains(a.EmployeeId))
            .Select(a => new { a.Id, a.EmployeeId, TypeId = a.DeductionTypeId, a.Amount, a.IsActive })
            .ToListAsync(ct);

        // GOSI rate from the single company profile (default 9.75%).
        var gosiRate = await ctx.CompanyProfiles.Select(c => (decimal?)c.GosiRate).FirstOrDefaultAsync(ct) ?? 9.75m;

        // Gather every master-data id referenced across the page, resolve in one query.
        var mdIds = new HashSet<Guid>();
        void Add(Guid? id) { if (id.HasValue) mdIds.Add(id.Value); }
        foreach (var e in employees)
        {
            Add(e.JobTitleId); Add(e.NationalityId); Add(e.ContractTypeId); Add(e.EmploymentTypeId);
            Add(e.PaymentMethodId); Add(e.BankId); Add(e.WorkLocationId); Add(e.LeavePolicyId); Add(e.PayrollGroupId);
        }
        foreach (var a in allowances) mdIds.Add(a.AllowanceTypeId);
        foreach (var a in additions) mdIds.Add(a.TypeId);
        foreach (var a in deductions) mdIds.Add(a.TypeId);

        var md = await ctx.MasterDataItems
            .Where(m => mdIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Code, m.NameEn, m.NameAr, m.MetadataJson })
            .ToDictionaryAsync(m => m.Id, ct);

        // Allowance-type rules (max cap + GOSI inclusion) parsed once from master-data metadata.
        var allowMeta = new Dictionary<Guid, (decimal? Max, bool Gosi)>();
        foreach (var a in allowances)
            if (!allowMeta.ContainsKey(a.AllowanceTypeId) && md.TryGetValue(a.AllowanceTypeId, out var m))
                allowMeta[a.AllowanceTypeId] = ParseAllowanceRules(m.MetadataJson);

        var depIds = employees.Where(e => e.DepartmentId.HasValue).Select(e => e.DepartmentId!.Value).ToHashSet();
        var brIds = employees.Where(e => e.BranchId.HasValue).Select(e => e.BranchId!.Value).ToHashSet();
        var mgrIds = employees.Where(e => e.ManagerId.HasValue).Select(e => e.ManagerId!.Value).ToHashSet();

        var deps = await ctx.Departments.Where(d => depIds.Contains(d.Id))
            .Select(d => new { d.Id, Name = d.NameAr ?? d.Name }).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var brs = await ctx.Branch.Where(b => brIds.Contains(b.Id))
            .Select(b => new { b.Id, Name = b.NameAr ?? b.Name }).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var mgrs = await ctx.Employees.Where(m => mgrIds.Contains(m.Id))
            .Select(m => new { m.Id, First = m.FirstNameAr ?? m.FirstName, Last = m.LastNameAr ?? m.LastName })
            .ToDictionaryAsync(m => m.Id, ct);

        string? En(Guid? id) => id.HasValue && md.TryGetValue(id.Value, out var x) ? x.NameEn : null;
        string? Ar(Guid? id) => id.HasValue && md.TryGetValue(id.Value, out var x) ? x.NameAr : null;
        string? Code(Guid? id) => id.HasValue && md.TryGetValue(id.Value, out var x) ? x.Code : null;

        var allowancesByEmp = allowances
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => new EmployeeAllowanceDto
            {
                Id = a.Id,
                AllowanceTypeId = a.AllowanceTypeId,
                AllowanceType = En(a.AllowanceTypeId),
                AllowanceTypeAr = Ar(a.AllowanceTypeId),
                Amount = a.Amount,
                IsActive = a.IsActive,
            }).ToList());

        var additionsByEmp = additions
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => new EmployeeCompItemDto
            {
                Id = a.Id, TypeId = a.TypeId, Type = En(a.TypeId), TypeAr = Ar(a.TypeId), Code = Code(a.TypeId),
                Amount = a.Amount, IsActive = a.IsActive,
            }).ToList());
        var deductionsByEmp = deductions
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => new EmployeeCompItemDto
            {
                Id = a.Id, TypeId = a.TypeId, Type = En(a.TypeId), TypeAr = Ar(a.TypeId), Code = Code(a.TypeId),
                Amount = a.Amount, IsActive = a.IsActive,
            }).ToList());

        var list = employees.Select(e => new EmployeeDto
        {
            Id = e.Id,
            EmployeeNumber = e.EmployeeNumber,
            FirstName = e.FirstName,
            FirstNameAr = e.FirstNameAr,
            LastName = e.LastName,
            LastNameAr = e.LastNameAr,
            Email = e.Email,
            Phone = e.Phone,
            Gender = e.Gender.ToString(),
            GenderAr = MapGenderAr(e.Gender),
            DateOfBirth = e.DateOfBirth,
            NationalId = e.NationalId,
            NationalityId = e.NationalityId,
            Nationality = En(e.NationalityId),
            NationalityAr = Ar(e.NationalityId),
            Status = e.Status.ToString(),
            StatusAr = MapStatusAr(e.Status),
            ContractTypeId = e.ContractTypeId,
            ContractType = En(e.ContractTypeId),
            ContractTypeAr = Ar(e.ContractTypeId),
            EmploymentTypeId = e.EmploymentTypeId,
            EmploymentType = En(e.EmploymentTypeId),
            EmploymentTypeAr = Ar(e.EmploymentTypeId),
            HireDate = e.HireDate,
            TerminationDate = e.TerminationDate,
            JobTitleId = e.JobTitleId,
            JobTitle = En(e.JobTitleId),
            JobTitleAr = Ar(e.JobTitleId),
            DepartmentId = e.DepartmentId,
            DepartmentName = e.DepartmentId.HasValue && deps.TryGetValue(e.DepartmentId.Value, out var dn) ? dn : null,
            BranchId = e.BranchId,
            BranchName = e.BranchId.HasValue && brs.TryGetValue(e.BranchId.Value, out var bn) ? bn : null,
            ManagerId = e.ManagerId,
            ManagerName = e.ManagerId.HasValue && mgrs.TryGetValue(e.ManagerId.Value, out var m)
                ? BuildManagerName(m.First, m.Last) : null,
            Address = e.Address,
            City = e.City,
            EmergencyContactName = e.EmergencyContactName,
            EmergencyContactPhone = e.EmergencyContactPhone,
            BasicSalary = e.BasicSalary,
            Currency = e.Currency,
            GosiEnabled = e.GosiEnabled,
            GosiRateOverride = e.GosiRateOverride,
            PaymentMethodId = e.PaymentMethodId,
            PaymentMethod = En(e.PaymentMethodId),
            PaymentMethodAr = Ar(e.PaymentMethodId),
            PaymentMethodCode = Code(e.PaymentMethodId),
            BankId = e.BankId,
            Bank = En(e.BankId),
            BankAr = Ar(e.BankId),
            BankAccountNumber = e.BankAccountNumber,
            Iban = e.Iban,
            SalaryCardNumber = e.SalaryCardNumber,
            CardProvider = e.CardProvider,
            WorkLocationId = e.WorkLocationId,
            WorkLocation = En(e.WorkLocationId),
            WorkLocationAr = Ar(e.WorkLocationId),
            LeavePolicyId = e.LeavePolicyId,
            LeavePolicy = En(e.LeavePolicyId),
            LeavePolicyAr = Ar(e.LeavePolicyId),
            PayrollGroupId = e.PayrollGroupId,
            PayrollGroup = En(e.PayrollGroupId),
            PayrollGroupAr = Ar(e.PayrollGroupId),
            PhotoUrl = e.PhotoUrl,
            Notes = e.Notes,
            Allowances = allowancesByEmp.TryGetValue(e.Id, out var al) ? al : new List<EmployeeAllowanceDto>(),
            Additions = additionsByEmp.TryGetValue(e.Id, out var ad) ? ad : new List<EmployeeCompItemDto>(),
            Deductions = deductionsByEmp.TryGetValue(e.Id, out var de) ? de : new List<EmployeeCompItemDto>(),
            CreatedAt = e.CreatedAt,
        }).ToList();

        // Compute the salary breakdown (single source of truth): basic + allowances + additions − deductions − GOSI.
        // Allowance-type rules apply: an allowance is capped at its MaxAmount, and only allowances
        // flagged "include in GOSI" join the basic salary in the GOSI contribution base.
        foreach (var dto in list)
        {
            decimal totalAllowances = 0m, gosiAllowanceBase = 0m;
            foreach (var a in dto.Allowances.Where(a => a.IsActive))
            {
                var amount = a.Amount;
                var rule = allowMeta.TryGetValue(a.AllowanceTypeId, out var r) ? r : (Max: (decimal?)null, Gosi: false);
                if (rule.Max is { } max && max > 0 && amount > max) amount = max;   // cap
                totalAllowances += amount;
                if (rule.Gosi) gosiAllowanceBase += amount;                          // GOSI inclusion
            }

            dto.TotalAllowances = totalAllowances;
            dto.TotalAdditions = dto.Additions.Where(a => a.IsActive).Sum(a => a.Amount);
            dto.TotalDeductions = dto.Deductions.Where(a => a.IsActive).Sum(a => a.Amount);
            // Effective GOSI rate: employee override → tenant default, or 0 when disabled for the employee.
            var effectiveGosiRate = HR.Domain.Engines.Finance.GosiCalculation.EffectiveRate(
                dto.GosiEnabled, dto.GosiRateOverride, gosiRate);
            dto.GosiRate = effectiveGosiRate;
            dto.GosiAmount = Math.Round((dto.BasicSalary + gosiAllowanceBase) * effectiveGosiRate / 100m, 2);
            dto.GrossSalary = dto.BasicSalary + dto.TotalAllowances + dto.TotalAdditions;
            dto.NetSalary = dto.GrossSalary - dto.TotalDeductions - dto.GosiAmount;
        }
        return list;
    }

    /// <summary>Reads the allowance-type rule flags (max cap + GOSI inclusion) from master-data metadata JSON.</summary>
    private static (decimal? Max, bool Gosi) ParseAllowanceRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, false);
            decimal? max = null; bool gosi = false;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.NameEquals("maxAmount") && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var mx))
                    max = mx;
                else if (p.NameEquals("gosiApplicable") && (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False))
                    gosi = p.Value.GetBoolean();
            }
            return (max, gosi);
        }
        catch { return (null, false); }
    }

    private static string? BuildManagerName(string? first, string? last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string MapGenderAr(Gender gender) => gender switch
    {
        Gender.Male => "ذكر",
        Gender.Female => "أنثى",
        _ => ""
    };

    private static string MapStatusAr(EmployeeStatus status) => status switch
    {
        EmployeeStatus.Active => "نشط",
        EmployeeStatus.OnLeave => "في إجازة",
        EmployeeStatus.Suspended => "موقوف",
        EmployeeStatus.Terminated => "منتهي",
        EmployeeStatus.Resigned => "مستقيل",
        _ => ""
    };
}
