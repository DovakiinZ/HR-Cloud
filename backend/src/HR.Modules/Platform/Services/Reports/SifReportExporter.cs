using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Application.Engines.Finance.Export.Bank;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Projects a report's rows through the existing Saudi WPS/SIF bank profile to produce a
/// WPS SIF CSV file. The report must expose the canonical WPS column codes; otherwise a
/// <see cref="ValidationException"/> (400) names what is missing.</summary>
public static class SifReportExporter
{
    private static readonly string[] Required =
        { "EmployeeNumber", "NationalId", "EmployeeName", "Iban", "BankCode", "NetAmount", "Currency" };

    public static byte[] Export(ReportResult result)
    {
        // ── 1. Column-presence check ──────────────────────────────────────────────────────
        var present = result.Columns.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = Required.Where(r => !present.Contains(r)).ToList();
        if (missing.Count > 0)
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    "columns",
                    $"Report is missing WPS columns: {string.Join(", ", missing)}.")
            });

        // ── 2. Collect flat rows (top-level or recursed from groups) ───────────────────
        var dataRows = CollectRows(result);
        var payments = dataRows.Select(ToPayment).ToList();

        // ── 3. Run WPS/SIF validator ──────────────────────────────────────────────────
        var errors = new SaudiWpsSifValidator().Validate(payments);
        if (errors.Count > 0)
            throw new ValidationException(errors.Select(e =>
                new ValidationFailure(e.Field, $"{e.EmployeeNumber}: {e.Message}")).ToArray());

        // ── 4. Map through the bank profile and write CSV ─────────────────────────────
        var dataset = BankFieldMapper.Map(payments, new SaudiWpsSifProfile());
        return new CsvExportWriter().Write(dataset);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static IEnumerable<ReportRow> CollectRows(ReportResult result)
    {
        if (result.Rows.Count > 0) return result.Rows;
        var acc = new List<ReportRow>();
        void Walk(IEnumerable<ReportGroup> groups)
        {
            foreach (var g in groups)
            {
                if (g.SubGroups.Count > 0) Walk(g.SubGroups);
                else acc.AddRange(g.Rows);
            }
        }
        Walk(result.Groups);
        return acc;
    }

    private static BankPaymentRow ToPayment(ReportRow row)
    {
        string S(string code) => row.TryGetValue(code, out var v) ? v?.ToString() ?? "" : "";
        decimal D(string code) => row.TryGetValue(code, out var v) && v is not null
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;
        return new BankPaymentRow(
            EmployeeNumber: S("EmployeeNumber"),
            EmployeeName: S("EmployeeName"),
            Iban: S("Iban"),
            BankCode: S("BankCode"),
            NationalId: S("NationalId"),
            NetAmount: D("NetAmount"),
            Currency: S("Currency"));
    }
}
