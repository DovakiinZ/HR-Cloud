using HR.Domain.Engines.Reports;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Resolves a filter's effective value at run time.
///
/// A filter marked IsParameter treats its stored Value as a default that the caller may override
/// per run; a filter that is not parameterized always uses its stored value, so a caller cannot
/// rewrite a constraint the report's author fixed deliberately.</summary>
public static class ReportParameterBinder
{
    /// <summary>Suffix for the upper bound of a Between filter, e.g. "Salary:to".</summary>
    public const string UpperBoundSuffix = ":to";

    public static (string? Value, string? ValueTo) Resolve(
        ReportFilter filter,
        IReadOnlyDictionary<string, string?>? parameters)
    {
        if (!filter.IsParameter || parameters is null || parameters.Count == 0)
            return (filter.Value, filter.ValueTo);

        var value = TryGet(parameters, filter.FieldCode, out var supplied) ? supplied : filter.Value;
        var valueTo = TryGet(parameters, filter.FieldCode + UpperBoundSuffix, out var suppliedTo) ? suppliedTo : filter.ValueTo;
        return (value, valueTo);
    }

    /// <summary>Field codes resolve case-insensitively, matching how the row/evaluation context
    /// resolves them everywhere else in the engine.</summary>
    private static bool TryGet(IReadOnlyDictionary<string, string?> parameters, string key, out string? value)
    {
        if (parameters.TryGetValue(key, out value)) return true;

        foreach (var kv in parameters)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
