using FluentAssertions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>The join rules the SQL builder and resolver silently tolerate rather than reject.
/// Kept DB-free so they get real coverage (the command-level tests are DB-gated and skip locally).</summary>
public class ReportRelationshipRulesTests
{
    private static readonly Guid Primary = Guid.NewGuid();
    private static readonly Guid Department = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();

    private static string? Validate(
        Guid source, Guid target, string joinType = "Inner", int sortOrder = 0,
        params ReportRelationshipRules.Existing[] existing)
        => ReportRelationshipRules.Validate(Primary, existing, source, target, joinType, sortOrder);

    [Fact]
    public void Join_from_the_primary_object_is_valid()
        => Validate(Primary, Department).Should().BeNull();

    [Theory]
    [InlineData("Inner")]
    [InlineData("left")]
    [InlineData("RIGHT")]
    public void Supported_join_types_are_accepted_case_insensitively(string joinType)
        => Validate(Primary, Department, joinType).Should().BeNull();

    [Theory]
    [InlineData("Full")]
    [InlineData("Cross")]
    [InlineData("")]
    public void Unsupported_join_type_is_rejected(string joinType)
    {
        // ReportSqlBuilder maps anything unrecognized to INNER JOIN via its `_ =>` arm,
        // so without this guard a typo silently becomes a different query.
        Validate(Primary, Department, joinType).Should().Contain("not supported");
    }

    [Fact]
    public void Self_join_is_rejected()
        => Validate(Department, Department).Should().Contain("different objects");

    [Fact]
    public void Joining_the_same_target_twice_is_rejected()
    {
        // The resolver does aliasByObjectId[TargetObjectId] = alias, so a second join to the same
        // object overwrites the first and only the last alias is addressable by fields.
        Validate(Primary, Department, "Inner", 1, new ReportRelationshipRules.Existing(Department, Primary, 0))
            .Should().Contain("already joined");
    }

    [Fact]
    public void Primary_object_cannot_be_a_join_target()
        => Validate(Department, Primary).Should().Contain("primary object");

    [Fact]
    public void Source_that_is_joined_earlier_is_valid()
        => Validate(Department, Location, "Inner", 1, new ReportRelationshipRules.Existing(Department, Primary, 0))
            .Should().BeNull();

    [Fact]
    public void Source_joined_at_a_higher_sort_order_is_rejected()
    {
        // Aliases are assigned walking SortOrder ascending, and sourceAlias falls back to "t0" via
        // GetValueOrDefault -- so an out-of-order source silently joins the primary table instead.
        Validate(Department, Location, "Inner", 0, new ReportRelationshipRules.Existing(Department, Primary, 5))
            .Should().Contain("joined earlier");
    }

    [Fact]
    public void Source_that_is_not_joined_at_all_is_rejected()
        => Validate(Department, Location).Should().Contain("joined earlier");
}
