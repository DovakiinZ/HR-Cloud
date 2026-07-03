using System.Text.Json;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Finance.Payslips;

/// <summary>One presentable line of a payslip, projected from the immutable ComponentsJson snapshot.
/// <paramref name="Code"/> is the raw component key (e.g. "BASIC" or "TXN:{id}"); <paramref name="ComponentCode"/>
/// is the display/component key. Labels (bilingual) are resolved at a higher layer from master data.</summary>
public sealed record PayslipComponentLine(string Code, string ComponentCode, PayComponentKind Kind, decimal Amount);

/// <summary>The grouped, presentable breakdown of a payslip: applied earnings and deductions in execution
/// order, plus totals. Contribution/Information components (which don't affect the employee's net) are excluded.</summary>
public sealed record PayslipBreakdown(
    IReadOnlyList<PayslipComponentLine> Earnings,
    IReadOnlyList<PayslipComponentLine> Deductions,
    decimal TotalEarnings,
    decimal TotalDeductions,
    decimal NetAmount);

/// <summary>Pure projection of a payslip's ComponentsJson (written by PayrollRunEngine.CalculateAsync as
/// <c>{ "order": [...], "components": [ ComponentResult, ... ] }</c>) into a grouped breakdown. Applies the
/// same rules as <c>PayslipLedgerMapper</c>: only components that are Applied and non-zero, and only the
/// Earning/Deduction kinds, are surfaced — keeping the payslip faithful to the ledger.</summary>
public static class PayslipComponentProjection
{
    private static readonly PayslipBreakdown Empty =
        new(Array.Empty<PayslipComponentLine>(), Array.Empty<PayslipComponentLine>(), 0m, 0m, 0m);

    public static PayslipBreakdown Project(string? componentsJson)
    {
        if (string.IsNullOrWhiteSpace(componentsJson)) return Empty;

        using var doc = JsonDocument.Parse(componentsJson);
        if (!doc.RootElement.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array)
            return Empty;

        var earnings = new List<PayslipComponentLine>();
        var deductions = new List<PayslipComponentLine>();
        decimal totalEarnings = 0m, totalDeductions = 0m;

        foreach (var c in comps.EnumerateArray())
        {
            var applied = c.TryGetProperty("Applied", out var ap) && ap.ValueKind == JsonValueKind.True;
            if (!applied) continue;

            var amount = c.TryGetProperty("Amount", out var am) && am.TryGetDecimal(out var d) ? d : 0m;
            if (amount == 0m) continue;

            var kind = c.TryGetProperty("Kind", out var k) && k.TryGetInt32(out var ki)
                ? (PayComponentKind)ki : PayComponentKind.Information;
            if (kind is not (PayComponentKind.Earning or PayComponentKind.Deduction)) continue;

            var code = (c.TryGetProperty("Code", out var cd) ? cd.GetString() : null) ?? "COMP";
            var componentCode = (c.TryGetProperty("ComponentCode", out var cc) ? cc.GetString() : null) ?? code;

            var line = new PayslipComponentLine(code, componentCode, kind, Math.Abs(amount));
            if (kind == PayComponentKind.Earning) { earnings.Add(line); totalEarnings += line.Amount; }
            else { deductions.Add(line); totalDeductions += line.Amount; }
        }

        return new PayslipBreakdown(earnings, deductions, totalEarnings, totalDeductions,
            totalEarnings - totalDeductions);
    }
}
