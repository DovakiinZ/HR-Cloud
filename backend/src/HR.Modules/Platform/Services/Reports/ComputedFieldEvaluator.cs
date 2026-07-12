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
        var ctx = MutableEvaluationContext.FromFacts(row);
        var result = _evaluator.Evaluate(ast, ctx);
        return ToClr(result);
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
    /// Default finance built-ins + report helpers: today, now, age, yearsBetween, concat, coalesce, round.
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

        // coalesce(a, b, …) → first non-null argument (alias for engine's COALESCE, lowercase)
        reg.Register("coalesce", args =>
        {
            foreach (var a in args)
                if (!a.IsNull) return a;
            return RuleValue.Null;
        });

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
