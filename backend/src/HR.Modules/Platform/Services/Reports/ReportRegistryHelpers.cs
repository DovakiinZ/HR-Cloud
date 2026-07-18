using HR.Application.Reports.Registry;

namespace HR.Modules.Platform.Services.Reports;

public static class ReportRegistryHelpers
{
    private static readonly string[] DisplayPriority =
        { "NameAr","Name","NameEn","TitleAr","Title","DisplayName","FullName","Code","Number","EmployeeNumber","EmployeeName" };

    /// <summary>Pick a target object's display column from its available columns, by priority.</summary>
    public static string? PickDisplayColumn(IReadOnlyCollection<string> columns)
    {
        foreach (var p in DisplayPriority)
            foreach (var c in columns)
                if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return c;
        return columns.FirstOrDefault();
    }

    /// <summary>Allowed filter operators for a catalog data type (business-friendly, engine-supported).</summary>
    public static IReadOnlyList<string> OperatorsFor(string dataType) => dataType switch
    {
        "Number" or "Decimal" or "Currency" or "Percentage" =>
            new[]{ "Equals","NotEquals","GreaterThan","GreaterThanOrEqual","LessThan","LessThanOrEqual","Between" },
        "Date" or "DateTime" => new[]{ "Equals","Between","GreaterThan","LessThan" },
        "Boolean" => new[]{ "Equals" },
        "Reference" or "Enum" => new[]{ "Equals","NotEquals","In" },
        _ => new[]{ "Equals","NotEquals","Contains","StartsWith","EndsWith","In" }, // Text/Guid/other
    };
}
