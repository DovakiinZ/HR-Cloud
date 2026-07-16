using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Finance.Expressions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>Authoring path for computed fields: formula text in, AST JSON out.
/// The read path (ReportObjectResolver) only ever sees AST JSON, so the contract that matters
/// is that whatever we store deserializes and evaluates.</summary>
public class ReportFormulaCompilerTests
{
    [Fact]
    public void Compiles_formula_text_to_ast_json_that_round_trips()
    {
        var json = ReportFormulaCompiler.Compile("ROUND(basicSalary * 0.09, 2)", null);

        json.Should().NotBeNullOrWhiteSpace();
        var ast = AstJson.Deserialize(json!);            // the resolver's exact call
        var value = new ComputedFieldEvaluator().Evaluate(ast, new Dictionary<string, object?>
        {
            ["basicSalary"] = 10000m,
        });
        value.Should().Be(900m);
    }

    [Fact]
    public void Variables_resolve_case_insensitively_against_row_field_codes()
    {
        var ast = AstJson.Deserialize(ReportFormulaCompiler.Compile("BasicSalary + 1", null)!);
        new ComputedFieldEvaluator()
            .Evaluate(ast, new Dictionary<string, object?> { ["basicsalary"] = 5m })
            .Should().Be(6m);
    }

    [Fact]
    public void Invalid_formula_raises_ValidationException_not_ExpressionException()
    {
        // An unhandled ExpressionException maps to a 500; a user typo must be a 400.
        FluentActions.Invoking(() => ReportFormulaCompiler.Compile("basicSalary +", null))
            .Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainKey("CalculationText");
    }

    [Fact]
    public void Pre_serialized_ast_json_is_accepted_unchanged_for_backward_compatibility()
    {
        var existing = AstJson.Serialize(ExpressionParser.Parse("1 + 2"));
        ReportFormulaCompiler.Compile(null, existing).Should().Be(existing);
    }

    [Fact]
    public void Formula_text_wins_over_supplied_ast_json()
    {
        var stale = AstJson.Serialize(ExpressionParser.Parse("1 + 2"));
        var json = ReportFormulaCompiler.Compile("10 + 5", stale);

        new ComputedFieldEvaluator()
            .Evaluate(AstJson.Deserialize(json!), new Dictionary<string, object?>())
            .Should().Be(15m);
    }

    [Fact]
    public void No_formula_and_no_json_compiles_to_null()
        => ReportFormulaCompiler.Compile(null, null).Should().BeNull();

    [Fact]
    public void Whitespace_only_formula_is_treated_as_absent()
        => ReportFormulaCompiler.Compile("   ", null).Should().BeNull();
}
