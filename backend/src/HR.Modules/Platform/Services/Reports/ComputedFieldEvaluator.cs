using System.Globalization;
using HR.Domain.Engines.Finance.Expressions;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>
/// Evaluates a computed-field AST against a materialized report row, reusing the Finance expression
/// engine. Pure and deterministic. Variables resolve to the row's field values via RuleValue.From().
/// </summary>
/// <remarks>
/// API notes vs. the brief's assumptions:
/// - RuleValue has no Date kind (only Null/Number/Boolean/Text). Dates are stored as ISO-8601 text.
/// - RuleValue has no ToClr() — values are extracted by kind.
/// - RuleValue.Null is a static field, not Null() method.
/// - RuleFunction delegate takes IReadOnlyList&lt;RuleValue&gt;, not RuleValue[].
/// - MutableEvaluationContext.FromFacts() already converts object? → RuleValue, so no custom RowContext needed.
/// - today/now return an ISO-8601 text; age/yearsBetween parse their text arguments as DateTime.
/// </remarks>
public sealed class ComputedFieldEvaluator
{
    private readonly ExpressionEvaluator _evaluator;

    public ComputedFieldEvaluator(FunctionRegistry? functions = null)
        => _evaluator = new ExpressionEvaluator(functions ?? ReportFunctions());

    /// <summary>Evaluates the AST against the row and returns a CLR value (decimal, string, bool, or null).</summary>
    public object? Evaluate(Expr ast, IReadOnlyDictionary<string, object?> row)
    {
        // Normalize date-like CLR values to ISO-8601 strings so age()/yearsBetween() can parse them back.
        // RuleValue.From() has no DateTime arm and would fall through to locale-dependent ToString().
        var normalized = NormalizeDateValues(row);
        var ctx = MutableEvaluationContext.FromFacts(normalized);
        var result = _evaluator.Evaluate(ast, ctx);
        return ToClr(result);
    }

    /// <summary>
    /// Projects the row into a new dictionary with date-like CLR values converted to ISO-8601 strings.
    /// All other values are passed through unchanged.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> NormalizeDateValues(IReadOnlyDictionary<string, object?> row)
    {
        Dictionary<string, object?>? normalized = null;
        foreach (var kv in row)
        {
            string? iso = kv.Value switch
            {
                DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => null,
            };
            if (iso is not null)
            {
                normalized ??= new Dictionary<string, object?>(row);
                normalized[kv.Key] = iso;
            }
        }
        return normalized ?? row;
    }

    private static object? ToClr(RuleValue v) => v.Kind switch
    {
        RuleValueKind.Null => null,
        RuleValueKind.Number => v.AsNumber(),
        RuleValueKind.Boolean => v.AsBool(),
        RuleValueKind.Text => v.AsText(),
        _ => null,
    };

    /// <summary>
    /// Default finance built-ins + report helpers: today, now, age, yearsBetween, concat, round.
    /// coalesce/COALESCE is already registered by FunctionRegistry.CreateDefault() (case-insensitive lookup).
    /// Dates are represented as ISO-8601 text values (no Date RuleValueKind exists in this engine version).
    /// </summary>
    public static FunctionRegistry ReportFunctions()
    {
        var reg = FunctionRegistry.CreateDefault();

        // today() → ISO-8601 text of today's UTC date
        reg.Register("today", _ => RuleValue.Text(DateTime.UtcNow.Date.ToString("O")));

        // now() → ISO-8601 text of the current UTC instant
        reg.Register("now", _ => RuleValue.Text(DateTime.UtcNow.ToString("O")));

        // age(dateText) → integer years between dateText and today
        reg.Register("age", args =>
        {
            var from = ParseDate(args[0].AsText(), "age");
            return RuleValue.Number(YearsBetween(from, DateTime.UtcNow));
        });

        // yearsBetween(fromDateText, toDateText) → integer years between the two dates
        reg.Register("yearsBetween", args =>
        {
            var from = ParseDate(args[0].AsText(), "yearsBetween");
            var to = ParseDate(args[1].AsText(), "yearsBetween");
            return RuleValue.Number(YearsBetween(from, to));
        });

        // concat(a, b, …) → concatenated text of all arguments
        reg.Register("concat", args =>
            RuleValue.Text(string.Concat(args.Select(v => v.AsText()))));

        // coalesce is already registered as COALESCE by FunctionRegistry.CreateDefault() with a proper
        // arity guard, and the registry lookup is case-insensitive — no custom registration needed.

        // round(value, digits) → rounded number
        reg.Register("round", args =>
        {
            var digits = args.Count >= 2 ? (int)args[1].AsNumber() : 2;
            return RuleValue.Number(Math.Round(args[0].AsNumber(), digits, MidpointRounding.AwayFromZero));
        });

        return reg;
    }

    private static DateTime ParseDate(string text, string callerName)
    {
        if (DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        throw new ExpressionException($"{callerName}: cannot parse '{text}' as a date.");
    }

    private static decimal YearsBetween(DateTime from, DateTime to)
    {
        var years = to.Year - from.Year;
        if (to < from.AddYears(years)) years--;
        return years;
    }
}
