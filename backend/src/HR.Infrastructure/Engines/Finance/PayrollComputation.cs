using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>The shared, write-free heart of preview and calculation: resolve inputs, compile the pinned
/// rule set once, evaluate every employee, and surface any per-employee errors as warnings rather than
/// aborting the whole run. Reused by both the Preview Engine and the Run Engine so the two can never
/// drift apart.</summary>
public sealed record PayrollComputationResult(
    PayrollDefinitionVersion Version,
    IReadOnlyList<EmployeePayrollInput> Inputs,
    IReadOnlyList<EmployeePayrollResult> Results,
    RuleCompilationResult Compilation);

public sealed class PayrollComputation
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollFactProvider _facts;
    private readonly IRuleEngine _rules;
    private readonly IPayrollTransactionConsumer _consumer;

    public PayrollComputation(ApplicationDbContext db, IPayrollFactProvider facts, IRuleEngine rules,
        IPayrollTransactionConsumer consumer)
    {
        _db = db;
        _facts = facts;
        _rules = rules;
        _consumer = consumer;
    }

    public async Task<PayrollComputationResult> ComputeAsync(
        PayrollDefinitionVersion version, PayrollPeriod period,
        IReadOnlyCollection<Guid>? restrictToEmployeeIds = null, CancellationToken ct = default)
    {
        var inputs = await _facts.BuildInputsAsync(version, period, restrictToEmployeeIds, ct);

        RuleCompilationResult compilation;
        IRuleSetEvaluator? evaluator = null;

        if (version.RuleSetVersionId is { } ruleSetVersionId)
        {
            compilation = await _rules.ValidateAsync(ruleSetVersionId, ct);
            if (compilation.IsValid)
                evaluator = await _rules.CompileAsync(ruleSetVersionId, ct);
        }
        else
        {
            compilation = new RuleCompilationResult
            {
                IsValid = false,
                Errors = new[] { "No rule set is linked to this payroll definition version." },
            };
        }

        var results = new List<EmployeePayrollResult>(inputs.Count);
        if (evaluator is not null)
        {
            foreach (var input in inputs)
            {
                var warnings = new List<string>();
                RuleSetEvaluation evaluation;
                try
                {
                    evaluation = evaluator.Evaluate(input.Facts);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Calculation failed: {ex.Message}");
                    evaluation = new RuleSetEvaluation(
                        Array.Empty<ComponentResult>(), Array.Empty<string>(), 0m, 0m, 0m);
                }
                results.Add(new EmployeePayrollResult { Input = input, Evaluation = evaluation, Warnings = warnings });
            }
        }

        // 2C: consume approved addition/deduction records as per-record components (read-only, used by
        // both preview and calculate). PayrollTransaction is disjoint from EmployeeAdditions/Deductions,
        // so these are additive — no double count.
        if (results.Count > 0)
        {
            var empIds = results.Select(r => r.EmployeeId).ToList();
            var consumables = await _consumer.GetConsumableAsync(
                period.Year, period.Month, empIds, version.CutoffDay, version.CarryToNextPeriod, ct);
            if (consumables.Count > 0)
            {
                var byEmp = consumables.GroupBy(c => c.EmployeeId).ToDictionary(g => g.Key, g => (IReadOnlyList<ConsumableTransaction>)g.ToList());
                for (var i = 0; i < results.Count; i++)
                {
                    if (byEmp.TryGetValue(results[i].EmployeeId, out var txns))
                        results[i] = results[i] with { Evaluation = PayrollTransactionMerge.Apply(results[i].Evaluation, txns) };
                }
            }
        }

        return new PayrollComputationResult(version, inputs, results, compilation);
    }

    /// <summary>Returns the count of consumable transactions for the given population in the period.
    /// Used by the snapshot writer to populate <c>TransactionCountConsumed</c>.</summary>
    public async Task<int> GetConsumableCountAsync(
        PayrollDefinitionVersion version, PayrollPeriod period,
        IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0) return 0;
        var consumables = await _consumer.GetConsumableAsync(
            period.Year, period.Month, employeeIds, version.CutoffDay, version.CarryToNextPeriod, ct);
        return consumables.Count;
    }

    /// <summary>Other non-cancelled runs for the same definition whose period overlaps this one.</summary>
    public async Task<IReadOnlyList<(Guid RunId, DateTime Start, DateTime End)>> OverlappingRunsAsync(
        Guid definitionId, PayrollPeriod period, Guid? excludeRunId, CancellationToken ct)
    {
        var candidates = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.PayrollDefinitionId == definitionId
                        && r.State != HR.Domain.Enums.PayrollRunState.Cancelled
                        && (excludeRunId == null || r.Id != excludeRunId))
            .Select(r => new { r.Id, r.PeriodStart, r.PeriodEnd })
            .ToListAsync(ct);

        return candidates
            .Where(r => period.Overlaps(r.PeriodStart, r.PeriodEnd))
            .Select(r => (r.Id, r.PeriodStart, r.PeriodEnd))
            .ToList();
    }
}
