namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure validation for report joins.
///
/// The execution path is permissive in ways that produce wrong data rather than errors:
/// ReportSqlBuilder maps an unrecognized JoinType to INNER JOIN via its `_ =>` arm, and
/// ReportObjectResolver assigns aliases walking SortOrder ascending with a GetValueOrDefault(…, "t0")
/// fallback — so an out-of-order source silently joins the primary table. These rules reject at the
/// write path what the read path would otherwise quietly accept.</summary>
public static class ReportRelationshipRules
{
    /// <summary>The join types ReportSqlBuilder actually distinguishes.</summary>
    public static readonly string[] JoinTypes = { "Inner", "Left", "Right" };

    /// <summary>A join already on the report.</summary>
    public sealed record Existing(Guid TargetObjectId, Guid SourceObjectId, int SortOrder);

    /// <summary>Returns null when the candidate join is valid, else the reason.</summary>
    public static string? Validate(
        Guid primaryObjectId,
        IReadOnlyList<Existing> existing,
        Guid sourceObjectId,
        Guid targetObjectId,
        string joinType,
        int sortOrder)
    {
        if (sourceObjectId == targetObjectId)
            return "A join's source and target must be different objects.";

        if (!JoinTypes.Contains(joinType, StringComparer.OrdinalIgnoreCase))
            return $"Join type '{joinType}' is not supported; use Inner, Left, or Right.";

        if (targetObjectId == primaryObjectId)
            return "The primary object cannot be a join target.";

        if (existing.Any(e => e.TargetObjectId == targetObjectId))
            return "This object is already joined to the report; joining the same object twice is not supported in R1.";

        if (sourceObjectId != primaryObjectId &&
            !existing.Any(e => e.TargetObjectId == sourceObjectId && e.SortOrder < sortOrder))
            return "A join's source must be the primary object or an object joined earlier (a lower sort order).";

        return null;
    }
}
