using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Reports.Registry;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Reports;

// ── Inputs (already parsed from the request) ────────────────────────────────────

/// <summary>A user-authored filter expressed in registry business keys.</summary>
public sealed record QuickFilterInput(
    string FieldKey, string Operator, string? Value, string? ValueTo, bool IsParameter);

/// <summary>A user-authored sort expressed in a registry business key.</summary>
public sealed record QuickSortInput(string FieldKey, string Direction); // "Ascending" | "Descending"

// ── Plan (what the handler persists) ────────────────────────────────────────────

public sealed record QuickFieldSpec(
    Guid ObjectDefinitionId, string FieldCode, string LabelAr, string LabelEn,
    AggregationType? Aggregation, int SortOrder);

public sealed record QuickJoinSpec(
    Guid SourceObjectId, Guid TargetObjectId, string JoinField, int SortOrder);

public sealed record QuickFilterSpec(
    string FieldCode, ReportFilterOperator Operator, string? Value, string? ValueTo, bool IsParameter);

public sealed record QuickGroupSpec(string FieldCode, int SortOrder);

public sealed record QuickSortSpec(string FieldCode, SortDirection Direction, int SortOrder);

public sealed record QuickReportPlan(
    Guid PrimaryObjectId,
    string PrimaryObjectCode,
    IReadOnlyList<QuickFieldSpec> Fields,
    IReadOnlyList<QuickJoinSpec> Joins,
    IReadOnlyList<QuickFilterSpec> Filters,
    IReadOnlyList<QuickGroupSpec> Groupings,
    IReadOnlyList<QuickSortSpec> Sortings,
    bool HasAggregation,
    IReadOnlyList<string> SkippedFieldKeys,
    IReadOnlyList<string> SkippedFilterKeys);

/// <summary>
/// Pure, DB-free bridge from the Report Field Registry to an executable report shape.
/// It takes trusted business field keys (already resolved to <see cref="ReportFieldDescriptor"/>s)
/// and produces the exact <c>ReportField</c> / <c>ReportRelationship</c> / <c>ReportFilter</c>
/// rows the EXISTING <c>ReportObjectResolver</c> understands — automatically discovering the
/// join chain (Phase 4) instead of asking the user to model joins.
///
/// It is deliberately conservative so it can NEVER emit a definition the engine would reject:
///   • Fields that share a physical column (the engine uses FieldCode as both the column lookup
///     AND the output code, and hard-rejects duplicate output codes in R1) are skipped, not merged.
///   • Filters/sorts on joined-object fields are skipped (the R1 engine only filters/sorts the
///     primary object) rather than added and silently mis-evaluated.
///   • Every skipped key is reported back so the UI can tell the user exactly what was dropped.
/// The execution engine, SQL builder and saved reports are untouched.
/// </summary>
public static class QuickReportComposer
{
    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;

    public static QuickReportPlan Compose(
        IReadOnlyDictionary<string, ReportFieldDescriptor> byKey,
        IReadOnlyList<string> displayKeys,
        IReadOnlyList<QuickFilterInput> filters,
        IReadOnlyList<string> groupByKeys,
        IReadOnlyList<QuickSortInput> sorts,
        IReportObjectIdResolver ids)
    {
        // Display fields in the caller's order, ignoring keys the registry couldn't resolve
        // (unknown keys are surfaced separately by the handler via ReportResolveResult.UnknownKeys).
        var displayFields = displayKeys
            .Where(k => byKey.ContainsKey(k))
            .Select(k => (Key: k, Desc: byKey[k]))
            .ToList();

        if (displayFields.Count == 0)
            throw Invalid("fields", "No valid report fields were resolved from the provided keys.");

        // The primary object is the source of the first field's join chain (related field) or the
        // field's own object (own field). All fields must share it — the registry guarantees this
        // per subject, but we enforce it and skip strays rather than emit a broken multi-root report.
        var primaryCode = PrimaryCodeOf(displayFields[0].Desc);
        var primaryId = ids.ResolveId(primaryCode)
            ?? throw Invalid("subject", $"Primary object '{primaryCode}' has no ObjectDefinition; " +
                                        "seed reportable objects first.");

        var skippedFields = new List<string>();
        var usedColumns = new HashSet<string>(OIC);      // physical FieldCode collisions
        var includedKeys = new HashSet<string>(OIC);     // keys that made it in (for group-by gating)
        var fieldSpecs = new List<QuickFieldSpec>();
        var joinSpecs = new List<QuickJoinSpec>();
        var seenJoins = new HashSet<(string, string, string)>();  // (source,target,joinField) codes
        var wantAggregation = groupByKeys.Count > 0;

        var order = 0;
        foreach (var (key, d) in displayFields)
        {
            // 1. One subject per report.
            if (!OIC.Equals(PrimaryCodeOf(d), primaryCode))
            {
                skippedFields.Add(key);
                continue;
            }

            // 2. Physical-column collision (two joined "…Name" columns that map to the same column,
            //    e.g. Department.NameAr and JobTitle.NameAr) — keep the first, skip the rest.
            if (usedColumns.Contains(d.PropertyPath))
            {
                skippedFields.Add(key);
                continue;
            }

            // 3. Resolve every join hop to ObjectDefinition Guids BEFORE committing the field, so a
            //    field whose chain can't be built is skipped whole (never a dangling column).
            var pending = new List<(( string s, string t, string j) code, Guid src, Guid tgt)>();
            var joinOk = true;
            foreach (var step in d.JoinPath)
            {
                var code = (step.SourceObjectCode, step.TargetObjectCode, step.JoinField);
                if (seenJoins.Contains(code)) continue;
                var src = ids.ResolveId(step.SourceObjectCode);
                var tgt = ids.ResolveId(step.TargetObjectCode);
                if (src is null || tgt is null) { joinOk = false; break; }
                pending.Add((code, src.Value, tgt.Value));
            }
            if (!joinOk)
            {
                skippedFields.Add(key);
                continue;
            }

            // Commit. JoinPath is emitted from the primary outward, and we add fields in caller order,
            // so first-seen order is a valid topological order for ReportObjectResolver's alias chain.
            foreach (var p in pending)
            {
                seenJoins.Add(p.code);
                joinSpecs.Add(new QuickJoinSpec(p.src, p.tgt, p.code.j, joinSpecs.Count));
            }

            usedColumns.Add(d.PropertyPath);
            includedKeys.Add(key);

            // Aggregate measures only when the user asked for grouping; dimensions stay raw.
            AggregationType? agg = wantAggregation && d.Aggregatable ? ParseAggregation(d.DefaultAggregation) : null;

            fieldSpecs.Add(new QuickFieldSpec(
                d.ObjectDefinitionId, d.PropertyPath, d.LabelAr, d.LabelEn, agg, order++));
        }

        if (fieldSpecs.Count == 0)
            throw Invalid("fields", "None of the selected fields could be added to a report.");

        // ── Filters — primary-object fields only (R1 does not filter joined objects) ──
        var skippedFilters = new List<string>();
        var filterSpecs = new List<QuickFilterSpec>();
        foreach (var flt in filters)
        {
            if (!byKey.TryGetValue(flt.FieldKey, out var d)
                || d.JoinPath.Count > 0 || d.ObjectDefinitionId != primaryId
                || !Enum.TryParse<ReportFilterOperator>(flt.Operator, ignoreCase: true, out var op))
            {
                skippedFilters.Add(flt.FieldKey);
                continue;
            }
            filterSpecs.Add(new QuickFilterSpec(d.PropertyPath, op, flt.Value, flt.ValueTo, flt.IsParameter));
        }

        // ── Group-by — only on columns that were actually selected (their output code exists) ──
        var groupSpecs = new List<QuickGroupSpec>();
        var gi = 0;
        foreach (var gk in groupByKeys)
        {
            if (includedKeys.Contains(gk) && byKey.TryGetValue(gk, out var d))
                groupSpecs.Add(new QuickGroupSpec(d.PropertyPath, gi++));
        }

        // ── Sort — primary-object fields only (R1 sorts the primary; joined sorts are ignored) ──
        var sortSpecs = new List<QuickSortSpec>();
        var si = 0;
        foreach (var s in sorts)
        {
            if (!byKey.TryGetValue(s.FieldKey, out var d)
                || d.JoinPath.Count > 0 || d.ObjectDefinitionId != primaryId)
                continue;
            var dir = Enum.TryParse<SortDirection>(s.Direction, ignoreCase: true, out var parsed)
                ? parsed : SortDirection.Ascending;
            sortSpecs.Add(new QuickSortSpec(d.PropertyPath, dir, si++));
        }

        return new QuickReportPlan(
            primaryId, primaryCode, fieldSpecs, joinSpecs, filterSpecs, groupSpecs, sortSpecs,
            HasAggregation: wantAggregation && groupSpecs.Count > 0,
            SkippedFieldKeys: skippedFields,
            SkippedFilterKeys: skippedFilters);
    }

    /// <summary>Own field → its own object; related-display field → the source of its join chain.</summary>
    private static string PrimaryCodeOf(ReportFieldDescriptor d)
        => d.JoinPath.Count > 0 ? d.JoinPath[0].SourceObjectCode : d.ObjectCode;

    private static AggregationType ParseAggregation(string? name)
        => Enum.TryParse<AggregationType>(name, ignoreCase: true, out var a) ? a : AggregationType.Sum;

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
