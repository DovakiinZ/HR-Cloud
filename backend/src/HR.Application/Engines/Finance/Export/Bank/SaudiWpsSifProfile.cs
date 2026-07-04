namespace HR.Application.Engines.Finance.Export.Bank;

/// <summary>The Saudi Wage Protection System (WPS) Salary Information File (SIF) — the first registered
/// bank profile. The exact column set is bank/Mudad-specific; this is the standard SIF field set expressed
/// as data, so a specific bank layout is a new profile (or a future DB-backed override) with no engine
/// change. Amounts are formatted to 2 decimals per WPS convention.</summary>
public sealed class SaudiWpsSifProfile : IBankProfile
{
    public string Code => "SA_WPS_SIF";
    public string DisplayName => "Saudi WPS (SIF)";
    public string Version => "1.0";
    public ExportFormat Format => ExportFormat.Csv;

    public IReadOnlyList<BankField> Fields { get; } = new[]
    {
        new BankField("EmployeeNumber", "Employee ID", "EmployeeNumber"),
        new BankField("NationalId", "National / Iqama ID", "NationalId"),
        new BankField("EmployeeName", "Employee Name", "EmployeeName"),
        new BankField("Iban", "IBAN", "Iban"),
        new BankField("BankCode", "Bank Code", "BankCode"),
        new BankField("NetAmount", "Net Salary", "NetAmount", Format: "0.00"),
        new BankField("Currency", "Currency", "Currency"),
    };
}
