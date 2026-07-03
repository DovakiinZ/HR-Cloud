using HR.Application.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Derived run staleness check (no persistence side-effects). A run is stale when its frozen
/// payslip snapshot no longer matches the current consumable transaction set for the same period and
/// population. Two directions are checked:
/// — Forward gap: an Approved in-period transaction exists that is NOT in the snapshot (new txn added
///   after the last Calculate).
/// — Reverse gap: a TXN:{id:N} component in the snapshot is no longer consumable (e.g. the underlying
///   transaction was Reversed or Cancelled after the snapshot was taken).</summary>
public sealed class PayrollRunStalenessEvaluator : IPayrollRunStalenessEvaluator
{
    private readonly ApplicationDbContext _db;
    private readonly IPayrollTransactionConsumer _consumer;

    public PayrollRunStalenessEvaluator(ApplicationDbContext db, IPayrollTransactionConsumer consumer)
    {
        _db = db;
        _consumer = consumer;
    }

    public async Task<bool> IsStaleAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Run {runId} not found.");

        var ver = await _db.PayrollDefinitionVersions
            .FirstAsync(v => v.Id == run.PayrollDefinitionVersionId, ct);

        var empIds = await _db.PayrollRunPopulations
            .Where(p => p.PayrollRunId == runId && p.IsIncluded)
            .Select(p => p.EmployeeId)
            .ToListAsync(ct);

        if (empIds.Count == 0)
            return false; // empty population — nothing to check

        var consumable = await _consumer.GetConsumableAsync(
            run.TargetPeriodYear, run.TargetPeriodMonth,
            empIds,
            ver.CutoffDay, ver.CarryToNextPeriod,
            ct);

        var consumableIds = consumable.Select(c => c.TransactionId).ToHashSet();

        // TXN ids currently reflected in the payslip snapshot.
        var componentJsonList = await _db.PayrollPayslips
            .Where(p => p.PayrollRunId == runId)
            .Select(p => p.ComponentsJson)
            .ToListAsync(ct);

        var snapshotTxnIds = componentJsonList
            .SelectMany(ParseTxnIds)
            .ToHashSet();

        // Stale if a consumable txn isn't in the snapshot ...
        if (consumableIds.Except(snapshotTxnIds).Any()) return true;
        // ... or a snapshot txn is now Reversed / no longer consumable.
        if (snapshotTxnIds.Except(consumableIds).Any()) return true;

        return false;
    }

    /// <summary>Parses TXN component codes from a payslip's ComponentsJson.
    /// The serialised format (written by PayrollTransactionMerge.Apply via PayrollRunEngine.CalculateAsync)
    /// is: ComponentsJson = { "order": [...], "components": [{ "Code": "TXN:{id:N}", ... }, ...] }
    /// where :N is the 32-hex no-dashes Guid format, e.g. "TXN:4b3e1a2c3d4e5f6a7b8c9d0e1f2a3b4c".
    /// Confirmed at PayrollTransactionMerge.cs line 27:
    ///   var code = $"{ComponentCodePrefix}{t.TransactionId:N}";
    /// and verified by PayslipLedgerMapper.cs line 48-51 which parses the exact same format back.</summary>
    private static IEnumerable<Guid> ParseTxnIds(string? componentsJson)
    {
        if (string.IsNullOrWhiteSpace(componentsJson))
            yield break;

        System.Text.Json.JsonDocument? doc = null;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(componentsJson);
            if (!doc.RootElement.TryGetProperty("components", out var comps)
                || comps.ValueKind != System.Text.Json.JsonValueKind.Array)
                yield break;

            foreach (var c in comps.EnumerateArray())
            {
                if (!c.TryGetProperty("Code", out var codeProp)) continue;
                var code = codeProp.GetString();
                if (code is null || !code.StartsWith(PayrollTransactionMerge.ComponentCodePrefix, StringComparison.Ordinal))
                    continue;

                var suffix = code[PayrollTransactionMerge.ComponentCodePrefix.Length..];
                if (Guid.TryParseExact(suffix, "N", out var id))
                    yield return id;
            }
        }
        finally
        {
            doc?.Dispose();
        }
    }
}
