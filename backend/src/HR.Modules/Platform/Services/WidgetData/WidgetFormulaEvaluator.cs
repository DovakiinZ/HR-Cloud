using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HR.Domain.Engines.Finance.Expressions;
using HR.Modules.Platform.Services.Reports;

namespace HR.Modules.Platform.Services.WidgetData;

/// <summary>Evaluates a Calculated KPI formula over named measure values, reusing the reports
/// expression engine (ExpressionParser → ComputedFieldEvaluator). Pure and deterministic.</summary>
public static class WidgetFormulaEvaluator
{
    public static double Evaluate(string formula, IReadOnlyDictionary<string, double> measures)
    {
        Expr ast = ExpressionParser.Parse(formula); // throws ExpressionException on a bad formula
        var facts = measures.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        var result = new ComputedFieldEvaluator().Evaluate(ast, facts);
        return result is null ? 0d : Convert.ToDouble(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Null when the formula parses, else the reason (for the builder's live validation).</summary>
    public static string? Validate(string formula) => ReportFormulaCompiler.Validate(formula);
}
