using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Finance.Expressions;
using HR.Domain.Engines.ObjectRegistry;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Bridges report definitions (which reference objects by Guid via ObjectRegistry)
/// to the live catalog, validating every table/column/join against IObjectCatalogService.</summary>
public sealed class ReportObjectResolver : IReportObjectResolver
{
    private readonly ApplicationDbContext _db;
    private readonly IObjectCatalogService _catalog;

    public ReportObjectResolver(ApplicationDbContext db, IObjectCatalogService catalog)
    { _db = db; _catalog = catalog; }

    public async Task<ReportExecutionModel> BuildModelAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?>? parameters,
        CancellationToken ct)
    {
        // 1. Gather object Guids (primary + relationship targets/sources).
        var objectIds = new HashSet<Guid> { report.PrimaryObjectId };
        foreach (var rel in report.Relationships) { objectIds.Add(rel.SourceObjectId); objectIds.Add(rel.TargetObjectId); }
        var defs = await _db.Set<ObjectDefinition>().AsNoTracking()
            .Where(o => objectIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);

        ResolvedObject ResolveId(Guid id)
        {
            if (!defs.TryGetValue(id, out var def)) throw Invalid("object", $"Unknown object definition '{id}'.");
            return _catalog.Resolve(def.Code) ?? throw Invalid("object", $"Object '{def.Code}' is not discoverable.");
        }

        var primary = ResolveId(report.PrimaryObjectId);
        var query = new ReportQueryModel { Primary = primary, PrimaryAlias = "t0" };
        var aliasByObjectId = new Dictionary<Guid, string> { [report.PrimaryObjectId] = "t0" };

        // 2. Joins (ordered).
        var n = 1;
        foreach (var rel in report.Relationships.OrderBy(r => r.SortOrder))
        {
            var target = ResolveId(rel.TargetObjectId);
            var source = ResolveId(rel.SourceObjectId);
            if (source.Field(rel.JoinField) is null)
                throw Invalid("join", $"Join field '{rel.JoinField}' is not a field of '{source.Code}'.");
            var alias = "t" + n++;
            aliasByObjectId[rel.TargetObjectId] = alias;
            var sourceAlias = aliasByObjectId.GetValueOrDefault(rel.SourceObjectId, "t0");
            query.Joins.Add(new ReportJoinModel
            {
                Alias = alias, Target = target, SourceAlias = sourceAlias,
                SourceColumn = source.Field(rel.JoinField)!.ColumnName,
                TargetKeyColumn = target.KeyColumn, JoinType = rel.JoinType,
            });
        }

        string AliasFor(Guid? objId) => objId is { } id && aliasByObjectId.TryGetValue(id, out var a) ? a : "t0";
        ResolvedObject ObjFor(Guid? objId) => objId is { } id && id != report.PrimaryObjectId && defs.ContainsKey(id) ? ResolveId(id) : primary;

        var model = new ReportExecutionModel { Query = query };

        // 3. Fields → SQL columns / computed specs / output columns.
        foreach (var f in report.Fields.Where(f => f.IsVisible).OrderBy(f => f.SortOrder))
        {
            var col = new ReportColumn
            {
                Code = f.FieldCode, Label = f.DisplayNameAr, FormatPattern = f.FormatPattern,
                IsMeasure = f.Aggregation is not null, Aggregation = f.Aggregation,
            };
            model.OutputColumns.Add(col);

            if (f.FieldType == ReportFieldType.CalculatedField)
            {
                if (string.IsNullOrWhiteSpace(f.CalculationExpression))
                    throw Invalid("field", $"Computed field '{f.FieldCode}' has no expression.");
                model.Computed.Add(new ComputedColumnSpec { Code = f.FieldCode, Ast = ParseAst(f.CalculationExpression) });
                continue;
            }

            var obj = ObjFor(f.ObjectDefinitionId);
            var rf = obj.Field(f.FieldCode) ?? throw Invalid("field", $"Field '{f.FieldCode}' not found on '{obj.Code}'.");
            col.Type = rf.Kind.ToString();
            query.Columns.Add(new ReportColumnModel { TableAlias = AliasFor(f.ObjectDefinitionId), Field = rf, OutputCode = f.FieldCode });
        }

        // 3b. Guard against duplicate OutputCode (case-insensitive) among SQL columns.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in query.Columns)
        {
            if (!seen.Add(col.OutputCode))
                throw Invalid("field",
                    $"Report has duplicate field code '{col.OutputCode}' across selected objects; " +
                    "a report cannot select two fields with the same code in R1.");
        }

        // 4. Filters — resolve against the primary object and push to SQL.
        // A filter whose field is not on the primary object is an error, not a no-op: silently
        // dropping it returns unfiltered numbers that look authoritative. R1 does not filter on
        // joined objects' fields, so say so rather than answer the wrong question.
        foreach (var flt in report.Filters)
        {
            var rf = primary.Field(flt.FieldCode);
            if (rf is null)
                throw Invalid("filter",
                    $"Filter field '{flt.FieldCode}' is not a field of the primary object '{primary.Code}'; " +
                    "filtering on a joined object's field is not supported in R1.");

            var (value, valueTo) = ReportParameterBinder.Resolve(flt, parameters);
            query.Filters.Add(new ReportFilterModel
            {
                TableAlias = "t0", Field = rf, Operator = flt.Operator, Value = value, ValueTo = valueTo,
            });
        }

        // 5. Sortings: object fields → SQL ORDER BY; computed fields → in-memory.
        var computedCodes = model.Computed.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in report.Sortings.OrderBy(s => s.SortOrder))
        {
            if (computedCodes.Contains(s.FieldCode)) { model.InMemorySorts.Add((s.FieldCode, s.Direction)); continue; }
            var rf = primary.Field(s.FieldCode);
            if (rf is not null) query.Sorts.Add(new ReportSortModel { TableAlias = "t0", Field = rf, Direction = s.Direction });
        }

        // 6. Group-by codes (order by SortOrder).
        model.GroupByCodes = report.Groupings.OrderBy(g => g.SortOrder).Select(g => g.FieldCode).ToList();

        return model;
    }

    private static Expr ParseAst(string calculationExpression)
        => HR.Domain.Engines.Finance.Expressions.AstJson.Deserialize(calculationExpression);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
