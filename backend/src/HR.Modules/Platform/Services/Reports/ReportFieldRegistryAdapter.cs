using HR.Application.Reports.Registry;
using HR.Application.SemanticCatalog;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>
/// Converts the curated <see cref="ISemanticCatalogProvider"/> (ReportEnabled fields)
/// into executable <see cref="ReportFieldDescriptor"/>s by resolving live catalog
/// metadata and ObjectDefinition Guids. Built once in the constructor; all public
/// methods are read-only and thread-safe.
/// </summary>
public sealed class ReportFieldRegistryAdapter : IReportFieldRegistry
{
    // ── Constant permission map: subject (domain code) → required permission ──
    private static readonly Dictionary<string, string> SubjectPermission =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["employees"]   = "Employees.View",
            ["attendance"]  = "Attendance.View",
            ["payroll"]     = "Payroll.View",
            ["leaves"]      = "Leaves.View",
            ["requests"]    = "Requests.View",
            ["expenses"]    = "Payroll.View",
            ["loans"]       = "Payroll.View",
            ["documents"]   = "Employees.View",
        };
    private const string DefaultPermission = "Platform.Reports.View";
    private const int MaxJoinDepth = 3;

    // ── Built once ────────────────────────────────────────────────────────────
    private readonly IReadOnlyList<ReportSubjectDescriptor> _allSubjects;
    private readonly IReadOnlyDictionary<string, ReportFieldDescriptor> _byKey;      // all valid fields
    private readonly IReadOnlyDictionary<string, List<ReportFieldDescriptor>> _bySubject; // subject → sorted fields
    private readonly ReportRegistryHealth _health;

    public ReportFieldRegistryAdapter(
        ISemanticCatalogProvider semantic,
        IObjectCatalogService catalog,
        IReportObjectIdResolver ids,
        ILogger<ReportFieldRegistryAdapter> logger)
    {
        // ── Phase 1: build descriptor set ────────────────────────────────────
        var exclusions = new List<ReportRegistryExclusion>();
        var allDescriptors = new List<ReportFieldDescriptor>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Enumerate every object in the semantic catalog
        var allCtx = new CatalogQueryContext(Array.Empty<string>()); // unrestricted scan
        var domains = semantic.GetDomains(allCtx)
            .ToDictionary(d => d.Code, d => d, StringComparer.OrdinalIgnoreCase);
        var objects = semantic.GetObjects(allCtx);

        foreach (var semObj in objects)
        {
            var subject = semObj.DomainCode;
            var requiredPerm = SubjectPermission.TryGetValue(subject, out var p) ? p : DefaultPermission;

            foreach (var field in semObj.Fields.Where(f => f.ReportEnabled))
            {
                var exclusionKey = $"{field.ObjectCode}.{field.FieldCode}";

                // 1. Resolve ObjectDefinition id for the field's own object
                var ownId = ids.ResolveId(field.ObjectCode);
                if (ownId is null)
                {
                    Exclude(exclusions, exclusionKey, $"object '{field.ObjectCode}' has no ObjectDefinition");
                    continue;
                }

                // 2. Look up live catalog field
                var liveObj = catalog.GetObject(field.ObjectCode);
                var liveField = liveObj?.Fields.FirstOrDefault(f =>
                    string.Equals(f.Code, field.FieldCode, StringComparison.OrdinalIgnoreCase));
                if (liveField is null)
                {
                    Exclude(exclusions, exclusionKey, $"field '{field.FieldCode}' not on '{field.ObjectCode}'");
                    continue;
                }

                ReportFieldDescriptor descriptor;

                if (liveField.IsReference && !string.IsNullOrEmpty(liveField.ReferenceObjectCode))
                {
                    // ── Reference field → related-display descriptor ──────────
                    var targetCode = liveField.ReferenceObjectCode!;

                    // Resolve target object
                    var targetObj = catalog.GetObject(targetCode);
                    if (targetObj is null)
                    {
                        Exclude(exclusions, exclusionKey,
                            $"reference target '{targetCode}' not in catalog");
                        continue;
                    }

                    var targetId = ids.ResolveId(targetCode);
                    if (targetId is null)
                    {
                        Exclude(exclusions, exclusionKey,
                            $"object '{targetCode}' has no ObjectDefinition");
                        continue;
                    }

                    var displayCol = ReportRegistryHelpers.PickDisplayColumn(
                        targetObj.Fields.Select(f => f.Code).ToList());
                    if (displayCol is null)
                    {
                        Exclude(exclusions, exclusionKey,
                            $"target '{targetCode}' has no displayable columns");
                        continue;
                    }

                    // Build join path: find path from the subject's primary object to target
                    var primaryObjectCode = field.ObjectCode; // the object that owns the FK
                    var joinPath = ResolveJoinPath(primaryObjectCode, targetCode, catalog, exclusionKey,
                        exclusions, out var joinFailed);
                    if (joinFailed) continue;

                    // Derive key: strip trailing "Id" from field code → camelCase → "{subject}.{refName}Name"
                    var refName = StripTrailingId(field.FieldCode);
                    var key = $"{subject}.{ToCamel(refName)}Name";

                    if (!usedKeys.Add(key))
                    {
                        Exclude(exclusions, exclusionKey, $"duplicate key '{key}'");
                        continue;
                    }

                    descriptor = new ReportFieldDescriptor(
                        Key: key,
                        LabelAr: field.NameAr,
                        LabelEn: field.NameEn,
                        Subject: subject,
                        Group: field.GroupCode,
                        DataType: "Text",
                        ObjectDefinitionId: targetId.Value,
                        ObjectCode: targetCode,
                        PropertyPath: displayCol,
                        JoinPath: joinPath,
                        AllowedOperators: ReportRegistryHelpers.OperatorsFor("Text"),
                        Filterable: true,
                        Sortable: true,
                        Groupable: true,
                        Aggregatable: false,
                        DefaultAggregation: null,
                        IsDefault: field.DefaultVisible,
                        DisplayOrder: 0,
                        FormatPattern: null,
                        RequiredPermission: requiredPerm);
                }
                else
                {
                    // ── Own (non-reference) field ─────────────────────────────
                    var key = $"{subject}.{ToCamel(field.FieldCode)}";
                    if (!usedKeys.Add(key))
                    {
                        Exclude(exclusions, exclusionKey, $"duplicate key '{key}'");
                        continue;
                    }

                    var dataType = liveField.FieldType;
                    var aggregatable = liveField.IsMeasure;

                    descriptor = new ReportFieldDescriptor(
                        Key: key,
                        LabelAr: field.NameAr,
                        LabelEn: field.NameEn,
                        Subject: subject,
                        Group: field.GroupCode,
                        DataType: dataType,
                        ObjectDefinitionId: ownId.Value,
                        ObjectCode: field.ObjectCode,
                        PropertyPath: field.FieldCode,
                        JoinPath: Array.Empty<ReportJoinStep>(),
                        AllowedOperators: ReportRegistryHelpers.OperatorsFor(dataType),
                        Filterable: liveField.IsFilterable,
                        Sortable: true,
                        Groupable: liveField.IsGroupable,
                        Aggregatable: aggregatable,
                        DefaultAggregation: aggregatable ? "Sum" : null,
                        IsDefault: field.DefaultVisible,
                        DisplayOrder: 0,
                        FormatPattern: null,
                        RequiredPermission: requiredPerm);
                }

                allDescriptors.Add(descriptor);
            }
        }

        // ── Phase 2: build lookup structures ─────────────────────────────────
        _byKey = allDescriptors.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

        _bySubject = allDescriptors
            .GroupBy(d => d.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(d => d.DisplayOrder).ThenBy(d => d.Key).ToList(),
                StringComparer.OrdinalIgnoreCase);

        // ── Phase 3: subject descriptors ─────────────────────────────────────
        _allSubjects = domains.Values
            .Where(d => _bySubject.ContainsKey(d.Code))
            .OrderBy(d => d.SortOrder)
            .Select(d => new ReportSubjectDescriptor(d.Code, d.NameAr, d.NameEn, d.Icon, d.SortOrder))
            .ToList();

        // ── Phase 4: health ───────────────────────────────────────────────────
        _health = new ReportRegistryHealth(
            VisibleSubjects: _allSubjects.Count,
            VisibleFields: allDescriptors.Count,
            ExcludedFields: exclusions.Count,
            Exclusions: exclusions);

        // ── Phase 5: log summary ──────────────────────────────────────────────
        logger.LogDebug(
            "ReportFieldRegistryAdapter: {Fields} fields across {Subjects} subjects, {Excluded} excluded.",
            allDescriptors.Count, _allSubjects.Count, exclusions.Count);
        foreach (var ex in exclusions)
            logger.LogDebug("  EXCLUDED {Key}: {Reason}", ex.Key, ex.Reason);
    }

    // ── IReportFieldRegistry ──────────────────────────────────────────────────

    public IReadOnlyList<ReportSubjectDescriptor> GetSubjects(ReportRegistryContext ctx)
        => _allSubjects
            .Where(s => HasSubjectPermission(s.Key, ctx))
            .ToList();

    public IReadOnlyList<ReportFieldDescriptor> GetFields(ReportRegistryContext ctx, string subject)
    {
        if (!_bySubject.TryGetValue(subject, out var list)) return Array.Empty<ReportFieldDescriptor>();
        return list.Where(f => ctx.Permissions.Contains(f.RequiredPermission)).ToList();
    }

    public ReportFieldDescriptor? GetField(ReportRegistryContext ctx, string key)
    {
        if (!_byKey.TryGetValue(key, out var f)) return null;
        return ctx.Permissions.Contains(f.RequiredPermission) ? f : null;
    }

    public ReportResolveResult Resolve(ReportRegistryContext ctx, IReadOnlyCollection<string> keys)
    {
        var matched = new List<ReportFieldDescriptor>();
        var unknown = new List<string>();

        foreach (var key in keys)
        {
            var f = GetField(ctx, key);
            if (f is null) unknown.Add(key);
            else matched.Add(f);
        }

        // Deduplicate join steps
        var joins = matched
            .SelectMany(f => f.JoinPath)
            .GroupBy(j => (j.SourceObjectCode, j.TargetObjectCode, j.JoinField))
            .Select(g => g.First())
            .ToList();

        return new ReportResolveResult(matched, joins, unknown);
    }

    public ReportRegistryHealth GetHealth() => _health;

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void Exclude(List<ReportRegistryExclusion> list, string key, string reason)
        => list.Add(new ReportRegistryExclusion(key, reason));

    private bool HasSubjectPermission(string subjectKey, ReportRegistryContext ctx)
    {
        var perm = SubjectPermission.TryGetValue(subjectKey, out var p) ? p : DefaultPermission;
        return ctx.Permissions.Contains(perm);
    }

    /// <summary>BFS join-path resolution from primaryObjectCode → targetObjectCode.</summary>
    private static IReadOnlyList<ReportJoinStep> ResolveJoinPath(
        string primaryObjectCode, string targetObjectCode,
        IObjectCatalogService catalog,
        string exclusionKey,
        List<ReportRegistryExclusion> exclusions,
        out bool failed)
    {
        // If the primary object IS the target (shouldn't normally happen), no path needed
        if (string.Equals(primaryObjectCode, targetObjectCode, StringComparison.OrdinalIgnoreCase))
        {
            failed = false;
            return Array.Empty<ReportJoinStep>();
        }

        // BFS over catalog references up to MaxJoinDepth
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryObjectCode };
        // Queue: (currentObjectCode, path so far)
        var queue = new Queue<(string code, List<ReportJoinStep> path)>();
        queue.Enqueue((primaryObjectCode, new List<ReportJoinStep>()));

        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();

            if (path.Count >= MaxJoinDepth) continue;

            var liveObj = catalog.GetObject(current);
            if (liveObj is null) continue;

            foreach (var f in liveObj.Fields.Where(f => f.IsReference && !string.IsNullOrEmpty(f.ReferenceObjectCode)))
            {
                var next = f.ReferenceObjectCode!;
                var step = new ReportJoinStep(current, next, f.Code);
                var newPath = new List<ReportJoinStep>(path) { step };

                if (string.Equals(next, targetObjectCode, StringComparison.OrdinalIgnoreCase))
                {
                    failed = false;
                    return newPath;
                }

                if (!visited.Contains(next))
                {
                    visited.Add(next);
                    queue.Enqueue((next, newPath));
                }
            }
        }

        // No path found
        Exclude(exclusions, exclusionKey,
            $"no relationship path '{primaryObjectCode}'→'{targetObjectCode}'");
        failed = true;
        return Array.Empty<ReportJoinStep>();
    }

    /// <summary>Strip a trailing "Id" suffix (case-insensitive) from a field code.</summary>
    private static string StripTrailingId(string code)
        => code.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && code.Length > 2
            ? code[..^2]
            : code;

    /// <summary>Convert PascalCase or camelCase to camelCase (first char lowercase).</summary>
    private static string ToCamel(string s)
        => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
