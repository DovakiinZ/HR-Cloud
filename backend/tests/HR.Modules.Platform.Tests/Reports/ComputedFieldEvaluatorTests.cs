using FluentAssertions;
using HR.Domain.Engines.Finance.Expressions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ComputedFieldEvaluatorTests
{
    private readonly ComputedFieldEvaluator _eval = new();

    private static Expr Var(string n) => new VariableExpr(n);
    private static Expr Num(decimal n) => new LiteralExpr(RuleValue.Number(n));

    [Fact]
    public void Evaluates_arithmetic_over_row_fields()
    {
        // basicSalary - basicSalary * gosiRate
        var ast = new BinaryExpr(BinaryOp.Subtract, Var("basicSalary"),
            new BinaryExpr(BinaryOp.Multiply, Var("basicSalary"), Var("gosiRate")));
        var row = new Dictionary<string, object?> { ["basicSalary"] = 10000m, ["gosiRate"] = 0.09m };
        var result = _eval.Evaluate(ast, row);
        Convert.ToDecimal(result).Should().Be(9100m);
    }

    [Fact]
    public void Concat_builds_full_name()
    {
        var ast = new FunctionCallExpr("concat", new List<Expr>
        {
            Var("firstName"), new LiteralExpr(RuleValue.Text(" ")), Var("lastName")
        });
        var row = new Dictionary<string, object?> { ["firstName"] = "Sara", ["lastName"] = "Ali" };
        _eval.Evaluate(ast, row).Should().Be("Sara Ali");
    }

    [Fact]
    public void YearsBetween_computes_service_years()
    {
        var ast = new FunctionCallExpr("yearsBetween", new List<Expr>
        {
            Var("hireDate"), new FunctionCallExpr("today", new List<Expr>())
        });
        // hireDate stored as ISO text string (DateTime.ToString() via RuleValue.From → Text)
        var row = new Dictionary<string, object?> { ["hireDate"] = new DateTime(2020, 7, 12).ToString("O") };
        var years = Convert.ToInt32(_eval.Evaluate(ast, row));
        years.Should().BeGreaterThanOrEqualTo(5);
    }
}
