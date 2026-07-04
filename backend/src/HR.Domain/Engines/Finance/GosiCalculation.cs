namespace HR.Domain.Engines.Finance;

/// <summary>Resolves the effective GOSI rate for an employee: the per-employee override when set, otherwise
/// the tenant default; always 0 when GOSI is disabled for the employee. GOSI is always auto-computed from
/// this rate by the rule engine (GOSI = PERCENT(GosiBase, GosiRate)) — never entered as a manual deduction.</summary>
public static class GosiCalculation
{
    public static decimal EffectiveRate(bool enabled, decimal? overrideRate, decimal tenantDefault)
        => !enabled ? 0m : (overrideRate ?? tenantDefault);
}
