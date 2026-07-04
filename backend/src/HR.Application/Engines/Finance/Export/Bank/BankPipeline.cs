using System.Globalization;

namespace HR.Application.Engines.Finance.Export.Bank;

/// <summary>The canonical, bank-agnostic payment line for one employee in a run. Every bank profile maps
/// from these fields, so adding a bank never changes how the run produces payment data.</summary>
public sealed record BankPaymentRow(
    string EmployeeNumber,
    string EmployeeName,
    string? Iban,
    string? BankCode,
    string? NationalId,
    decimal NetAmount,
    string Currency);

/// <summary>One output field of a bank profile: which canonical <see cref="SourceKey"/> feeds it, its
/// header/position width, and an optional numeric format. Expressing the mapping as DATA (not code) is what
/// lets a new bank be a new profile instance with the engine unchanged.</summary>
public sealed record BankField(string OutputKey, string Header, string SourceKey, int? Width = null, string? Format = null);

/// <summary>A versioned, country-agnostic bank/WPS file definition. Saudi WPS/SIF is the first registered
/// profile; others are added by registering more instances.</summary>
public interface IBankProfile
{
    string Code { get; }
    string DisplayName { get; }
    string Version { get; }
    ExportFormat Format { get; }
    IReadOnlyList<BankField> Fields { get; }
}

public sealed record BankValidationError(string EmployeeNumber, string Field, string Message);

/// <summary>Validates payment rows against a profile's rules — deliberately SEPARATE from file generation
/// so a run can never emit an invalid bank file. DI-discovered by <see cref="ProfileCode"/>.</summary>
public interface IBankExportValidator
{
    string ProfileCode { get; }
    IReadOnlyList<BankValidationError> Validate(IReadOnlyList<BankPaymentRow> rows);
}

/// <summary>Pure projection of canonical payment rows through a profile's field list into a
/// <see cref="TabularDataset"/> ready for any <see cref="IExportWriter"/>.</summary>
public static class BankFieldMapper
{
    public static TabularDataset Map(IReadOnlyList<BankPaymentRow> rows, IBankProfile profile)
    {
        var columns = profile.Fields
            .Select(f => new TabularColumn(f.OutputKey, f.Header, TabularAlign.Start, f.Width))
            .ToList();

        var outRows = rows.Select(r =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var f in profile.Fields)
                dict[f.OutputKey] = FormatValue(Resolve(r, f.SourceKey), f.Format);
            return (IReadOnlyDictionary<string, object?>)dict;
        }).ToList();

        return new TabularDataset(profile.DisplayName, columns, outRows);
    }

    private static object? Resolve(BankPaymentRow r, string sourceKey) => sourceKey switch
    {
        "EmployeeNumber" => r.EmployeeNumber,
        "EmployeeName" => r.EmployeeName,
        "Iban" => r.Iban,
        "BankCode" => r.BankCode,
        "NationalId" => r.NationalId,
        "NetAmount" => r.NetAmount,
        "Currency" => r.Currency,
        _ => null,
    };

    private static object? FormatValue(object? v, string? format) =>
        v is decimal d && !string.IsNullOrEmpty(format) ? d.ToString(format, CultureInfo.InvariantCulture) : v;
}
