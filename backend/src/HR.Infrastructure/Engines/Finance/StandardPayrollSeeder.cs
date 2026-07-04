using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.Expressions;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Seeds a tenant's default "Standard Monthly" rule set + payroll definition so payroll is
/// runnable out of the box. Rules are authored in the expression language and compiled to AST on save,
/// exactly as a user-built rule set would be.</summary>
public sealed class StandardPayrollSeeder : IStandardPayrollSeeder
{
    private const string RuleSetCode = "STD_MONTHLY";
    private const string DefinitionCode = "MONTHLY";

    private readonly ApplicationDbContext _db;
    public StandardPayrollSeeder(ApplicationDbContext db) => _db = db;

    private sealed record RuleDef(string Code, string NameAr, PayComponentKind Kind, string Expr);

    // Facts come from the fact provider: BasicSalary, TotalAllowances, TotalAdditions, TotalDeductions,
    // GosiBase, GosiRate. Earnings build gross; deductions reduce net (aggregated by RuleEngineCore).
    // Attendance penalties are now visible PayrollTransaction records (sub-project 2D) and flow through
    // TotalDeductions — the former ATTENDANCE_DED rule is retired.
    private static readonly RuleDef[] Rules =
    {
        new("BASIC", "الراتب الأساسي", PayComponentKind.Earning, "BasicSalary"),
        new("ALLOWANCES", "البدلات", PayComponentKind.Earning, "TotalAllowances"),
        new("ADDITIONS", "الإضافات", PayComponentKind.Earning, "TotalAdditions"),
        new("EXPENSE", "مصروفات مستردة", PayComponentKind.Earning, "ExpenseReimbursement"),
        new("GOSI", "التأمينات الاجتماعية", PayComponentKind.Deduction, "ROUND(PERCENT(GosiBase, GosiRate), 2)"),
        new("LOAN", "قسط قرض", PayComponentKind.Deduction, "LoanInstallment"),
        new("DEDUCTIONS", "الاستقطاعات", PayComponentKind.Deduction, "TotalDeductions"),
    };

    public async Task<Guid> EnsureStandardMonthlyAsync(CancellationToken ct = default)
    {
        var definition = await _db.PayrollDefinitions.FirstOrDefaultAsync(d => d.Code == DefinitionCode, ct);

        // Rule set + published version with compiled rules.
        var ruleSet = await _db.FinanceRuleSets.FirstOrDefaultAsync(r => r.Code == RuleSetCode, ct);
        Guid ruleSetVersionId;
        if (ruleSet is null)
        {
            ruleSet = new RuleSet { Code = RuleSetCode, Name = "Standard Monthly", NameAr = "الرواتب الشهرية القياسية", Status = RuleSetStatus.Active };
            _db.FinanceRuleSets.Add(ruleSet);

            var version = new RuleSetVersion { RuleSetId = ruleSet.Id, VersionNumber = 1, Status = VersionStatus.Published, PublishedAt = DateTime.UtcNow };
            _db.FinanceRuleSetVersions.Add(version);

            var seq = 1;
            foreach (var r in Rules)
                _db.FinanceRules.Add(BuildRule(version.Id, r, seq++));
            ruleSet.CurrentVersionId = version.Id;
            ruleSetVersionId = version.Id;
        }
        else
        {
            ruleSetVersionId = ruleSet.CurrentVersionId
                ?? (await _db.FinanceRuleSetVersions.Where(v => v.RuleSetId == ruleSet.Id)
                        .OrderByDescending(v => v.VersionNumber).Select(v => v.Id).FirstAsync(ct));
        }

        // Idempotently top-up any rules missing from the active version (so an already-seeded tenant
        // picks up newly-added standard rules without a version bump).
        await EnsureRulesPresentAsync(ruleSetVersionId, ct);

        if (definition is not null && definition.CurrentVersionId is not null)
        {
            await _db.SaveChangesAsync(ct);
            return definition.Id;
        }

        // Look up master-data ids for PayrollTypeCategory(REGULAR) and PayrollExportFormat(PDF).
        // These rows are seeded by the master-data default seeder. If that hasn't run yet for this
        // tenant the lookups return null — the FKs are nullable, so seeding still succeeds; the
        // fields will be null until the next bootstrap re-run or a user sets them via the UI.
        var categoryId = await _db.MasterDataItems
            .Where(m => m.ObjectType == MasterDataObjectType.PayrollTypeCategory && m.Code == "REGULAR")
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        var pdfFormatId = await _db.MasterDataItems
            .Where(m => m.ObjectType == MasterDataObjectType.PayrollExportFormat && m.Code == "PDF")
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);

        // Definition + published version pinned to the rule set.
        if (definition is null)
        {
            definition = new PayrollDefinition { Code = DefinitionCode, Name = "Monthly Payroll", NameAr = "الرواتب الشهرية", Scope = PayrollScope.Company, Status = PayrollDefinitionStatus.Active };
            _db.PayrollDefinitions.Add(definition);
        }
        definition.CategoryId ??= categoryId;

        var defVersion = new PayrollDefinitionVersion
        {
            PayrollDefinitionId = definition.Id,
            VersionNumber = 1,
            Status = VersionStatus.Published,
            Frequency = PayFrequency.Monthly,
            RuleSetVersionId = ruleSetVersionId,
            Currency = "SAR",
            PublishedAt = DateTime.UtcNow,
            CutoffDay = 27,
            DayBasis = DayBasis.CalendarMonth,
            CarryToNextPeriod = true,
            DefaultExportFormatId = pdfFormatId,
            SelectionScopeJson = "{\"mode\":\"All\",\"include\":[],\"exclude\":[],\"includeEmployeeIds\":[],\"excludeEmployeeIds\":[]}",
        };
        _db.PayrollDefinitionVersions.Add(defVersion);
        definition.CurrentVersionId = defVersion.Id;

        await _db.SaveChangesAsync(ct);
        return definition.Id;
    }

    /// <summary>Adds any standard rule (by Code) not already present on the given rule-set version.</summary>
    private async Task EnsureRulesPresentAsync(Guid ruleSetVersionId, CancellationToken ct)
    {
        var existing = await _db.FinanceRules
            .Where(r => r.RuleSetVersionId == ruleSetVersionId)
            .Select(r => new { r.Code, r.Sequence })
            .ToListAsync(ct);
        var have = existing.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seq = existing.Count == 0 ? 1 : existing.Max(r => r.Sequence) + 1;

        foreach (var r in Rules)
        {
            if (have.Contains(r.Code)) continue;
            _db.FinanceRules.Add(BuildRule(ruleSetVersionId, r, seq++));
        }

        // 2D: retire the fact-based attendance rule — attendance deductions are now visible records.
        // Deactivate (never delete) any legacy ATTENDANCE_DED rule so historical runs stay auditable.
        var legacy = await _db.FinanceRules
            .Where(r => r.RuleSetVersionId == ruleSetVersionId && r.Code == "ATTENDANCE_DED" && r.IsActive)
            .ToListAsync(ct);
        foreach (var r in legacy) r.IsActive = false;
    }

    private static Rule BuildRule(Guid ruleSetVersionId, RuleDef r, int sequence)
    {
        var ast = ExpressionParser.Parse(r.Expr);
        return new Rule
        {
            RuleSetVersionId = ruleSetVersionId,
            Code = r.Code,
            Name = r.Code,
            NameAr = r.NameAr,
            Kind = r.Kind,
            Sequence = sequence,
            ExpressionText = r.Expr,
            ExpressionAstJson = AstJson.Serialize(ast),
            OutputComponentCode = r.Code,
            IsActive = true,
        };
    }
}
