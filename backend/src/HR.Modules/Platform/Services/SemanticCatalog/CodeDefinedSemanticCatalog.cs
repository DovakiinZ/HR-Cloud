using HR.Application.SemanticCatalog;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>
/// Validates the curated <see cref="CatalogRegistry"/> against the live
/// <see cref="IObjectCatalogService"/>, self-hiding broken items, filtering
/// metrics by caller permissions, exposing health diagnostics, and serving
/// Arabic-normalized synonym search.
/// </summary>
public sealed class CodeDefinedSemanticCatalog : ISemanticCatalogProvider
{
    // ── Validated view (built once in the constructor) ────────────────────────

    private readonly IReadOnlyList<SemanticObject> _visibleObjects;
    private readonly IReadOnlyList<SemanticMetric> _visibleMetrics;  // validation-passing; permissions applied per-call
    private readonly CatalogHealth _health;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CodeDefinedSemanticCatalog(
        IObjectCatalogService catalog,
        ILogger<CodeDefinedSemanticCatalog> logger)
    {
        var hidden = new List<HiddenItem>();

        // ── 1. Validate objects ───────────────────────────────────────────────
        var visibleObjects = new List<SemanticObject>();

        foreach (var obj in CatalogRegistry.Objects)
        {
            var liveObj = catalog.GetObject(obj.ObjectCode);
            if (liveObj is null)
            {
                hidden.Add(new HiddenItem("Object", obj.ObjectCode,
                    $"object '{obj.ObjectCode}' not found"));
                continue;
            }

            // Filter fields to those present on the live object
            var liveFieldCodes = new HashSet<string>(
                liveObj.Fields.Select(f => f.Code), StringComparer.Ordinal);

            var filteredFields = new List<SemanticField>();
            foreach (var field in obj.Fields)
            {
                if (liveFieldCodes.Contains(field.FieldCode))
                {
                    filteredFields.Add(field);
                }
                else
                {
                    hidden.Add(new HiddenItem("Field", $"{obj.ObjectCode}.{field.FieldCode}",
                        $"field '{field.FieldCode}' not found on '{obj.ObjectCode}'"));
                }
            }

            // Filter DefaultFilters to those whose FieldCode exists on the live object
            var filteredDefaultFilters = obj.DefaultFilters
                .Where(f => liveFieldCodes.Contains(f.FieldCode))
                .ToList();

            // Null out DefaultSort if its FieldCode no longer exists on the live object
            var validatedDefaultSort = obj.DefaultSort is not null &&
                liveFieldCodes.Contains(obj.DefaultSort.FieldCode)
                    ? obj.DefaultSort
                    : null;

            var validatedObj = obj with
            {
                Fields = filteredFields,
                DefaultFilters = filteredDefaultFilters,
                DefaultSort = validatedDefaultSort,
            };
            visibleObjects.Add(validatedObj);
        }

        _visibleObjects = visibleObjects;

        // ── 2. Validate metrics ───────────────────────────────────────────────
        var visibleMetrics = new List<SemanticMetric>();

        foreach (var metric in CatalogRegistry.Metrics)
        {
            var def = metric.Definition;

            // (a) Object must resolve
            var liveObj = catalog.GetObject(def.ObjectCode);
            if (liveObj is null)
            {
                hidden.Add(new HiddenItem("Metric", metric.Code,
                    $"object '{def.ObjectCode}' not found"));
                continue;
            }

            var liveFieldCodes = new HashSet<string>(
                liveObj.Fields.Select(f => f.Code), StringComparer.Ordinal);

            // (b) Every referenced field must exist on that object
            string? missingReason = null;

            // AggregationField (null or empty string means Count/no-field — skip)
            if (!string.IsNullOrEmpty(def.AggregationField))
            {
                if (!liveFieldCodes.Contains(def.AggregationField))
                {
                    missingReason = $"field '{def.AggregationField}' not found on '{def.ObjectCode}'";
                }
            }

            // Filter FieldCodes
            if (missingReason is null)
            {
                foreach (var filter in def.Filters)
                {
                    if (!string.IsNullOrEmpty(filter.FieldCode) &&
                        !liveFieldCodes.Contains(filter.FieldCode))
                    {
                        missingReason = $"field '{filter.FieldCode}' not found on '{def.ObjectCode}'";
                        break;
                    }
                }
            }

            // GroupByField
            if (missingReason is null &&
                !string.IsNullOrEmpty(def.GroupByField) &&
                !liveFieldCodes.Contains(def.GroupByField))
            {
                missingReason = $"field '{def.GroupByField}' not found on '{def.ObjectCode}'";
            }

            // Measures (Formula path)
            if (missingReason is null && def.Measures is not null)
            {
                foreach (var measure in def.Measures)
                {
                    if (!string.IsNullOrEmpty(measure.AggregationField) &&
                        !liveFieldCodes.Contains(measure.AggregationField))
                    {
                        missingReason = $"field '{measure.AggregationField}' not found on '{def.ObjectCode}'";
                        break;
                    }

                    if (missingReason is null)
                    {
                        foreach (var mf in measure.Filters)
                        {
                            if (!string.IsNullOrEmpty(mf.FieldCode) &&
                                !liveFieldCodes.Contains(mf.FieldCode))
                            {
                                missingReason = $"field '{mf.FieldCode}' not found on '{def.ObjectCode}'";
                                break;
                            }
                        }
                    }

                    if (missingReason is not null) break;
                }
            }

            if (missingReason is not null)
            {
                hidden.Add(new HiddenItem("Metric", metric.Code, missingReason));
                continue;
            }

            visibleMetrics.Add(metric);
        }

        _visibleMetrics = visibleMetrics;

        // ── 3. Build health ───────────────────────────────────────────────────
        var hiddenObjects = hidden.Count(h => h.Kind == "Object");
        var hiddenMetrics = hidden.Count(h => h.Kind == "Metric");

        _health = new CatalogHealth(
            VisibleObjects: visibleObjects.Count,
            HiddenObjects: hiddenObjects,
            VisibleMetrics: visibleMetrics.Count,
            HiddenMetrics: hiddenMetrics,
            Hidden: hidden);

        // ── 4. Log summary ────────────────────────────────────────────────────
        logger.LogDebug(
            "SemanticCatalog: {VisibleObjects} objects, {HiddenObjects} hidden; " +
            "{VisibleMetrics} metrics, {HiddenMetrics} hidden",
            visibleObjects.Count, hiddenObjects, visibleMetrics.Count, hiddenMetrics);

        foreach (var h in hidden)
        {
            logger.LogDebug("SemanticCatalog hidden [{Kind}] {Code}: {Reason}", h.Kind, h.Code, h.Reason);
        }
    }

    // ── ISemanticCatalogProvider ──────────────────────────────────────────────

    public IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext ctx)
        => CatalogRegistry.Domains;

    public IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext ctx, string? domainCode = null)
    {
        var result = _visibleObjects.AsEnumerable();
        if (domainCode is not null)
            result = result.Where(o => o.DomainCode == domainCode);
        return result.ToList();
    }

    public SemanticObject? GetObject(CatalogQueryContext ctx, string objectCode)
        => _visibleObjects.FirstOrDefault(o => o.ObjectCode == objectCode);

    public IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext ctx, string? domainCode = null)
    {
        var result = _visibleMetrics.AsEnumerable();
        if (domainCode is not null)
            result = result.Where(m => m.DomainCode == domainCode);
        result = result.Where(m => HasAllPermissions(m, ctx));
        return result.ToList();
    }

    public SemanticMetric? GetMetric(CatalogQueryContext ctx, string metricCode)
    {
        var metric = _visibleMetrics.FirstOrDefault(m => m.Code == metricCode);
        if (metric is null) return null;
        if (!HasAllPermissions(metric, ctx)) return null;
        return metric;
    }

    public IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext ctx, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SemanticSearchHit>();

        // Normalize query and expand synonyms
        var normalizedQuery = ArabicText.Normalize(query.Trim());
        var searchTokens = ExpandSynonyms(normalizedQuery);

        var hits = new List<SemanticSearchHit>();

        // Search visible objects
        foreach (var obj in _visibleObjects)
        {
            var score = ScoreItem(
                code: obj.ObjectCode,
                nameAr: obj.NameAr,
                nameEn: obj.NameEn,
                keywords: obj.Keywords,
                tokens: searchTokens);

            if (score > 0)
                hits.Add(new SemanticSearchHit("Object", obj.ObjectCode, obj.NameAr, obj.NameEn, score));
        }

        // Search visible metrics (with permission filter)
        foreach (var metric in _visibleMetrics)
        {
            if (!HasAllPermissions(metric, ctx)) continue;

            var score = ScoreItem(
                code: metric.Code,
                nameAr: metric.NameAr,
                nameEn: metric.NameEn,
                keywords: Array.Empty<string>(),
                tokens: searchTokens);

            if (score > 0)
                hits.Add(new SemanticSearchHit("Metric", metric.Code, metric.NameAr, metric.NameEn, score));
        }

        // Search visible fields across all visible objects
        foreach (var obj in _visibleObjects)
        {
            foreach (var field in obj.Fields)
            {
                var score = ScoreItem(
                    code: field.FieldCode,
                    nameAr: field.NameAr,
                    nameEn: field.NameEn,
                    keywords: field.Keywords,
                    tokens: searchTokens);

                if (score > 0)
                    hits.Add(new SemanticSearchHit("Field", field.FieldCode, field.NameAr, field.NameEn, score));
            }
        }

        return hits.OrderByDescending(h => h.Score).ToList();
    }

    public CatalogHealth GetHealth() => _health;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasAllPermissions(SemanticMetric metric, CatalogQueryContext ctx)
        => metric.RequiredPermissions.All(p => ctx.Permissions.Contains(p));

    /// <summary>
    /// Given a normalized query token, returns a set of tokens to match against
    /// (the query itself plus any synonym expansions, all normalized).
    /// </summary>
    private static IReadOnlySet<string> ExpandSynonyms(string normalizedQuery)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal) { normalizedQuery };

        if (CatalogRegistry.Synonyms.TryGetValue(normalizedQuery, out var expansions))
        {
            foreach (var exp in expansions)
                tokens.Add(ArabicText.Normalize(exp));
        }

        return tokens;
    }

    /// <summary>
    /// Scores an item based on how well the search tokens match its code, names, and keywords.
    /// Returns 0 if no match.
    /// </summary>
    private static double ScoreItem(
        string code,
        string nameAr,
        string nameEn,
        IReadOnlyList<string> keywords,
        IReadOnlySet<string> tokens)
    {
        var normCode   = ArabicText.Normalize(code);
        var normNameAr = ArabicText.Normalize(nameAr);
        var normNameEn = ArabicText.Normalize(nameEn);

        double best = 0;

        foreach (var token in tokens)
        {
            // Exact code match = highest
            if (normCode == token)
            {
                best = Math.Max(best, 1.0);
                continue;
            }

            // Exact name match
            if (normNameAr == token || normNameEn == token)
            {
                best = Math.Max(best, 0.9);
                continue;
            }

            // Code or name contains token
            if (normCode.Contains(token) || normNameAr.Contains(token) || normNameEn.Contains(token))
            {
                best = Math.Max(best, 0.7);
                continue;
            }

            // Keyword exact match
            if (keywords.Any(k => ArabicText.Normalize(k) == token))
            {
                best = Math.Max(best, 0.6);
                continue;
            }

            // Keyword contains token
            if (keywords.Any(k => ArabicText.Normalize(k).Contains(token)))
            {
                best = Math.Max(best, 0.4);
            }
        }

        return best;
    }
}
