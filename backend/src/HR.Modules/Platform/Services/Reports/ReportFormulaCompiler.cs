using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Finance.Expressions;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Compiles a computed field's authored formula text into the AST JSON that
/// ReportObjectResolver reads back via AstJson.Deserialize.
///
/// Mirrors RuleEngine.CompileExpression, which is the established text-or-AST precedent.
/// The parser is reused, not reimplemented: ExpressionParser is a complete infix parser
/// emitting the same Expr nodes the reports engine already evaluates.</summary>
public static class ReportFormulaCompiler
{
    /// <summary>Returns the AST JSON to persist, or null when the field has no expression.
    /// Authored text wins over pre-serialized JSON; JSON alone is accepted so fields written
    /// before the authoring path existed keep working.</summary>
    /// <exception cref="ValidationException">The formula does not parse.</exception>
    public static string? Compile(string? calculationText, string? calculationAstJson)
    {
        if (string.IsNullOrWhiteSpace(calculationText))
            return string.IsNullOrWhiteSpace(calculationAstJson) ? null : calculationAstJson;

        // TryValidate keeps a user typo a 400. An ExpressionException escaping to the pipeline
        // would surface as a 500, reading as a server crash rather than bad input.
        var error = ExpressionParser.TryValidate(calculationText);
        if (error is not null)
            throw new ValidationException(new[]
            {
                new ValidationFailure("CalculationText", $"Invalid formula: {error}")
            });

        return AstJson.Serialize(ExpressionParser.Parse(calculationText));
    }

    /// <summary>Null when the formula parses, else the reason. Lets the builder validate as the
    /// user types without saving, and keeps the Finance expression namespace out of the controller.</summary>
    public static string? Validate(string? calculationText)
        => string.IsNullOrWhiteSpace(calculationText)
            ? "A formula is required."
            : ExpressionParser.TryValidate(calculationText);
}
