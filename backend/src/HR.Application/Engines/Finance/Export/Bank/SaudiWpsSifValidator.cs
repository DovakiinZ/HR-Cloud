namespace HR.Application.Engines.Finance.Export.Bank;

/// <summary>Validates payment rows for the Saudi WPS/SIF profile: a valid Saudi IBAN (SA + 22 digits), a
/// positive net amount, and a bank code. Runs before generation so an invalid SIF is never produced.</summary>
public sealed class SaudiWpsSifValidator : IBankExportValidator
{
    public string ProfileCode => "SA_WPS_SIF";

    public IReadOnlyList<BankValidationError> Validate(IReadOnlyList<BankPaymentRow> rows)
    {
        var errors = new List<BankValidationError>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Iban))
                errors.Add(new BankValidationError(r.EmployeeNumber, "Iban", "IBAN مفقود"));
            else if (!IsValidSaudiIban(r.Iban))
                errors.Add(new BankValidationError(r.EmployeeNumber, "Iban", "صيغة IBAN غير صحيحة"));

            if (r.NetAmount <= 0m)
                errors.Add(new BankValidationError(r.EmployeeNumber, "NetAmount", "صافي الراتب يجب أن يكون أكبر من صفر"));

            if (string.IsNullOrWhiteSpace(r.BankCode))
                errors.Add(new BankValidationError(r.EmployeeNumber, "BankCode", "رمز البنك مفقود"));
        }
        return errors;
    }

    private static bool IsValidSaudiIban(string iban)
    {
        var s = iban.Replace(" ", "").ToUpperInvariant();
        return s.Length == 24
            && s.StartsWith("SA", StringComparison.Ordinal)
            && s.Skip(2).All(char.IsDigit);
    }
}
