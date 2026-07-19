using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.WidgetData;

/// <summary>
/// Object-driven aggregation engine. Resolves a query spec against the Object Catalog,
/// emits parameterized SQL (identifiers whitelisted from the model, values bound), and
/// applies tenant + soft-delete scoping automatically. Works for ANY discoverable object.
/// </summary>
public sealed class WidgetDataService : IWidgetDataService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IObjectCatalogService _catalog;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string NullKey = "__NULL__";

    public WidgetDataService(ApplicationDbContext db, ICurrentUserService user, IObjectCatalogService catalog)
    {
        _db = db; _user = user; _catalog = catalog;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public async Task<WidgetDataResult> ExecuteAsync(WidgetQuerySpec spec, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct)
    {
        var obj = Resolve(spec.ObjectCode);
        var filters = Combine(spec.Filters, dashboardFilters);

        if (string.Equals(spec.Aggregation, "Formula", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(spec.GroupByField))
            return await ExecuteFormulaScalarAsync(obj, spec, filters, ct);

        var agg = ParseAggregation(spec.Aggregation);
        if (string.IsNullOrWhiteSpace(spec.GroupByField))
            return await ExecuteScalarAsync(obj, agg, spec, filters, ct);
        return await ExecuteSeriesAsync(obj, agg, spec, filters, ct);
    }

    public async Task<WidgetDataResult> ExecuteWidgetAsync(Guid widgetId, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct)
    {
        var widget = await _db.DashboardWidgets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == widgetId, ct)
                     ?? throw new NotFoundException("DashboardWidget", widgetId);
        var spec = ParseSpec(widget.Configuration)
                   ?? throw Invalid("configuration", "Widget has no valid data configuration.");
        if (!string.IsNullOrWhiteSpace(spec.RequiredPermission) && !_user.Permissions.Contains(spec.RequiredPermission))
            throw new ForbiddenException("You do not have permission to view this widget.");
        return await ExecuteAsync(spec, dashboardFilters, ct);
    }

    public async Task<WidgetDataResult> GetRowsAsync(WidgetQuerySpec spec, string? segmentKey, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, int page, int pageSize, CancellationToken ct)
    {
        var obj = Resolve(spec.ObjectCode);
        var cat = _catalog.GetObject(obj.Code)!;
        var filters = Combine(spec.Filters, dashboardFilters);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var p = new Params();
        var where = BaseWhere(obj, "t", p);
        AppendFilters(where, obj, filters, "t", p);

        // Segment filter (drill into one chart slice).
        if (!string.IsNullOrEmpty(segmentKey) && !string.IsNullOrWhiteSpace(spec.GroupByField))
        {
            var gf = obj.Field(spec.GroupByField) ?? throw Invalid("groupBy", $"Unknown field '{spec.GroupByField}'.");
            if (segmentKey == NullKey)
                where.Add($"t.{Q(gf.ColumnName)} IS NULL");
            else if (IsDate(gf) && !string.IsNullOrWhiteSpace(spec.DateGranularity))
                where.Add($"date_trunc('{Granularity(spec.DateGranularity)}', t.{Q(gf.ColumnName)}) = {p.Add(ConvertValue(segmentKey, typeof(DateTime), gf))}");
            else
                where.Add($"t.{Q(gf.ColumnName)} = {p.Add(ConvertValue(segmentKey, gf.ClrType, gf))}");
        }

        // Curated display columns (non-FK, non-key) — generic selection from the catalog.
        var allowed = new HashSet<string> { "Text", "Enum", "Boolean", "Date", "DateTime", "Number", "Decimal", "Currency", "Percentage" };
        var cols = cat.Fields.Where(f => allowed.Contains(f.FieldType))
            .OrderBy(f => DrilldownPriority(f.Code)).ThenBy(f => f.IsReference ? 2 : f.IsMeasure ? 1 : 0).ThenBy(f => f.NameEn)
            .Take(10).ToList();
        if (cols.Count == 0)
            cols = cat.Fields.Take(10).ToList();

        var selectList = string.Join(", ", cols.Select(c => $"t.{Q(c.Code)}"));
        var table = TableRef(obj);
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var total = Convert.ToInt64(await ScalarAsync($"SELECT COUNT(*) FROM {table} t {whereSql}", p, ct) ?? 0L);

        var orderCol = cols.FirstOrDefault(c => c.FieldType is "Date" or "DateTime")?.Code ?? obj.KeyColumn;
        var sql = $"SELECT {selectList} FROM {table} t {whereSql} ORDER BY t.{Q(orderCol)} DESC OFFSET {(page - 1) * pageSize} LIMIT {pageSize}";

        var result = new WidgetDataResult
        {
            Kind = "table", ObjectCode = obj.Code, Aggregation = "None", GroupByField = spec.GroupByField,
            TotalCount = total, Page = page, PageSize = pageSize,
            Columns = cols.Select(c => new TableColumn { Code = c.Code, Label = c.NameAr, Type = c.FieldType }).ToList(),
        };

        var optionMaps = cols.ToDictionary(c => c.Code, c => c.Options?.ToDictionary(o => o.Value, o => o.Label));
        await ReadAsync(sql, p, ct, reader =>
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cols)
            {
                var ord = reader.GetOrdinal(c.Code);
                var val = reader.IsDBNull(ord) ? null : reader.GetValue(ord);
                row[c.Code] = FormatCell(val, c.FieldType, optionMaps[c.Code]);
            }
            result.Rows.Add(row);
        });

        return result;
    }

    // ── Scalar (KPI) ─────────────────────────────────────────────────────────

    private async Task<WidgetDataResult> ExecuteScalarAsync(ResolvedObject obj, AggKind agg, WidgetQuerySpec spec, List<WidgetFilterSpec> filters, CancellationToken ct)
    {
        var table = TableRef(obj);

        if (agg == AggKind.Percentage)
        {
            // numerator: rows matching all filters; denominator: tenant total (ignoring user filters)
            var pn = new Params();
            var wn = BaseWhere(obj, "t", pn);
            AppendFilters(wn, obj, filters, "t", pn);
            var num = Convert.ToDouble(await ScalarAsync($"SELECT COUNT(*) FROM {table} t {Where(wn)}", pn, ct) ?? 0d);

            var pd = new Params();
            var wd = BaseWhere(obj, "t", pd);
            var den = Convert.ToDouble(await ScalarAsync($"SELECT COUNT(*) FROM {table} t {Where(wd)}", pd, ct) ?? 0d);

            return Scalar(obj, spec, den > 0 ? Math.Round(num / den * 100, 2) : 0d);
        }

        var p = new Params();
        var where = BaseWhere(obj, "t", p);
        AppendFilters(where, obj, filters, "t", p);
        var aggExpr = AggregateExpr(obj, agg, spec.AggregationField, "t");
        var value = await ScalarAsync($"SELECT {aggExpr} FROM {table} t {Where(where)}", p, ct);
        return Scalar(obj, spec, value is null or DBNull ? 0d : Convert.ToDouble(value));
    }

    private static WidgetDataResult Scalar(ResolvedObject obj, WidgetQuerySpec spec, double value) => new()
    {
        Kind = "scalar", ObjectCode = obj.Code, Aggregation = spec.Aggregation, Value = value,
    };

    // ── Calculated KPI (formula over named measures) ─────────────────────────
    private async Task<WidgetDataResult> ExecuteFormulaScalarAsync(ResolvedObject obj, WidgetQuerySpec spec, List<WidgetFilterSpec> filters, CancellationToken ct)
    {
        if (spec.Measures.Count == 0 || string.IsNullOrWhiteSpace(spec.Formula))
            throw Invalid("formula", "A calculated widget needs at least one measure and a formula.");

        var table = TableRef(obj);
        var measures = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in spec.Measures)
        {
            if (string.IsNullOrWhiteSpace(m.Name)) throw Invalid("measure", "Each measure needs a name.");
            var aggKind = ParseAggregation(m.Aggregation);
            if (aggKind == AggKind.Percentage) throw Invalid("measure", "A measure cannot itself be a percentage/formula.");

            var p = new Params();
            var where = BaseWhere(obj, "t", p);
            var merged = new List<WidgetFilterSpec>(filters);
            merged.AddRange(m.Filters);
            AppendFilters(where, obj, merged, "t", p);
            var aggExpr = AggregateExpr(obj, aggKind, m.AggregationField, "t");
            var v = await ScalarAsync($"SELECT {aggExpr} FROM {table} t {Where(where)}", p, ct);
            measures[m.Name] = v is null or DBNull ? 0d : Convert.ToDouble(v);
        }

        double value;
        try { value = WidgetFormulaEvaluator.Evaluate(spec.Formula!, measures); }
        catch (HR.Domain.Engines.Finance.Expressions.ExpressionException ex) { throw Invalid("formula", $"Invalid formula: {ex.Message}"); }

        return Scalar(obj, spec, value);
    }

    // ── Series (charts) ──────────────────────────────────────────────────────

    private async Task<WidgetDataResult> ExecuteSeriesAsync(ResolvedObject obj, AggKind agg, WidgetQuerySpec spec, List<WidgetFilterSpec> filters, CancellationToken ct)
    {
        if (agg == AggKind.Percentage) agg = AggKind.Count; // percentage is a scalar concept
        var gf = obj.Field(spec.GroupByField) ?? throw Invalid("groupBy", $"Unknown field '{spec.GroupByField}'.");

        var p = new Params();
        var where = BaseWhere(obj, "t", p);
        AppendFilters(where, obj, filters, "t", p);

        string keyExpr, labelExpr, join = "", orderBy;
        bool dateKey = false;

        if (IsDate(gf) && !string.IsNullOrWhiteSpace(spec.DateGranularity))
        {
            keyExpr = $"date_trunc('{Granularity(spec.DateGranularity)}', t.{Q(gf.ColumnName)})";
            labelExpr = keyExpr;
            orderBy = "k ASC";
            dateKey = true;
        }
        else if (gf.IsReference && gf.Reference is { } r)
        {
            join = $"LEFT JOIN {Ref(r.PrincipalSchema, r.PrincipalTable)} p ON p.{Q(r.PrincipalKeyColumn)} = t.{Q(gf.ColumnName)}";
            keyExpr = $"t.{Q(gf.ColumnName)}";
            labelExpr = DisplayExpr(r, "p");
            orderBy = "v DESC";
        }
        else
        {
            keyExpr = $"t.{Q(gf.ColumnName)}";
            labelExpr = keyExpr;
            orderBy = "v DESC";
        }

        var aggExpr = AggregateExpr(obj, agg, spec.AggregationField, "t");
        var limit = Math.Clamp(spec.Limit ?? 50, 1, 500);
        var table = TableRef(obj);
        var sql = $@"SELECT {keyExpr} AS k, {labelExpr} AS lbl, {aggExpr} AS v
FROM {table} t {join} {Where(where)}
GROUP BY {keyExpr}, {labelExpr}
ORDER BY {orderBy}
LIMIT {limit}";

        var result = new WidgetDataResult { Kind = "series", ObjectCode = obj.Code, Aggregation = spec.Aggregation, GroupByField = spec.GroupByField };
        var enumMap = gf.ClrType.IsEnum ? _catalog.GetObject(obj.Code)!.Fields.FirstOrDefault(f => f.Code == gf.Code)?.Options?.ToDictionary(o => o.Value, o => o.Label) : null;

        await ReadAsync(sql, p, ct, reader =>
        {
            var rawKey = reader.IsDBNull(0) ? null : reader.GetValue(0);
            var rawLabel = reader.IsDBNull(1) ? null : reader.GetValue(1);
            var val = reader.IsDBNull(2) ? 0d : Convert.ToDouble(reader.GetValue(2));

            string key, label;
            if (dateKey && rawKey is DateTime dt)
            {
                key = dt.ToString("yyyy-MM-dd");
                label = FormatDateLabel(dt, spec.DateGranularity!);
            }
            else if (rawKey is null)
            {
                key = NullKey; label = "—";
            }
            else
            {
                key = rawKey.ToString() ?? "";
                label = enumMap is not null && int.TryParse(key, out var ev) && enumMap.TryGetValue(ev, out var el)
                    ? el
                    : (rawLabel?.ToString() is { Length: > 0 } l ? l : key);
            }
            result.Series.Add(new SeriesPoint { Key = key, Label = label, Value = val });
        });

        result.TotalCount = result.Series.Count;
        return result;
    }

    // ── SQL helpers ──────────────────────────────────────────────────────────

    private enum AggKind { Count, Sum, Average, Min, Max, DistinctCount, Percentage }

    private string AggregateExpr(ResolvedObject obj, AggKind agg, string? fieldCode, string alias)
    {
        if (agg is AggKind.Count or AggKind.Percentage) return "COUNT(*)::float8";
        var f = obj.Field(fieldCode) ?? throw Invalid("aggregationField", "This aggregation requires a field.");
        var col = $"{alias}.{Q(f.ColumnName)}";
        return agg switch
        {
            AggKind.DistinctCount => $"COUNT(DISTINCT {col})::float8",
            AggKind.Sum => RequireMeasure(f, $"COALESCE(SUM({col}),0)::float8"),
            AggKind.Average => RequireMeasure(f, $"COALESCE(AVG({col}),0)::float8"),
            AggKind.Min => RequireMeasure(f, $"MIN({col})::float8"),
            AggKind.Max => RequireMeasure(f, $"MAX({col})::float8"),
            _ => "COUNT(*)::float8",
        };
    }

    private static string RequireMeasure(ResolvedField f, string expr)
        => f.Kind is FieldKind.Number or FieldKind.Decimal or FieldKind.Currency or FieldKind.Percentage
            ? expr : throw Invalid("aggregationField", $"Field '{f.Code}' is not numeric.");

    private List<string> BaseWhere(ResolvedObject obj, string alias, Params p)
    {
        var where = new List<string>();
        if (obj.HasTenant) where.Add($"{alias}.{Q("TenantId")} = {p.Add(_user.TenantId)}");
        if (obj.HasSoftDelete) where.Add($"{alias}.{Q("IsDeleted")} = false");
        return where;
    }

    private void AppendFilters(List<string> where, ResolvedObject obj, IEnumerable<WidgetFilterSpec> filters, string alias, Params p)
    {
        foreach (var f in filters)
        {
            if (string.IsNullOrWhiteSpace(f.Field)) continue;
            var field = obj.Field(f.Field);
            if (field is null) continue; // unknown field → ignored (never injected)
            var col = $"{alias}.{Q(field.ColumnName)}";
            var op = (f.Operator ?? "eq").ToLowerInvariant();

            switch (op)
            {
                case "is_null": where.Add($"{col} IS NULL"); break;
                case "not_null": where.Add($"{col} IS NOT NULL"); break;
                case "contains": where.Add($"{col}::text ILIKE '%' || {p.Add(f.Value ?? "")} || '%'"); break;
                case "startswith": where.Add($"{col}::text ILIKE {p.Add((f.Value ?? "") + "%")}"); break;
                case "ne": where.Add($"{col} <> {p.Add(ConvertValue(f.Value, field.ClrType, field))}"); break;
                case "gt": where.Add($"{col} > {p.Add(ConvertValue(f.Value, field.ClrType, field))}"); break;
                case "gte": where.Add($"{col} >= {p.Add(ConvertValue(f.Value, field.ClrType, field))}"); break;
                case "lt": where.Add($"{col} < {p.Add(ConvertValue(f.Value, field.ClrType, field))}"); break;
                case "lte": where.Add($"{col} <= {p.Add(ConvertValue(f.Value, field.ClrType, field))}"); break;
                case "last_n_days":
                    var n = int.TryParse(f.Value, out var d) ? d : 30;
                    where.Add($"{col} >= (now() - ({p.Add(n)} * interval '1 day'))");
                    break;
                case "between":
                    var parts = (f.Value ?? "").Split(',', 2);
                    if (parts.Length == 2)
                        where.Add($"{col} BETWEEN {p.Add(ConvertValue(parts[0], field.ClrType, field))} AND {p.Add(ConvertValue(parts[1], field.ClrType, field))}");
                    break;
                case "in":
                    var vals = (f.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (vals.Length > 0)
                        where.Add($"{col} IN ({string.Join(",", vals.Select(v => p.Add(ConvertValue(v, field.ClrType, field))))})");
                    break;
                default: where.Add($"{col} = {p.Add(ConvertValue(f.Value, field.ClrType, field))}"); break;
            }
        }
    }

    private static string DisplayExpr(ResolvedReference r, string alias)
    {
        if (r.DisplayColumn is { } d) return $"{alias}.{Q(d)}::text";
        if (r.DisplayConcatColumns is { Length: > 0 } cc)
            return "concat_ws(' ', " + string.Join(", ", cc.Select(c => $"{alias}.{Q(c)}")) + ")";
        return $"{alias}.{Q(r.PrincipalKeyColumn)}::text";
    }

    private static string TableRef(ResolvedObject obj) => Ref(obj.Schema, obj.TableName);
    private static string Ref(string? schema, string table) => schema is { Length: > 0 } ? $"{Q(schema)}.{Q(table)}" : Q(table);
    private static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
    private static string Where(List<string> w) => w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "";

    private static string Granularity(string g) => g.ToLowerInvariant() switch
    {
        "day" or "week" or "month" or "quarter" or "year" => g.ToLowerInvariant(),
        _ => "month",
    };

    // ── ADO execution ────────────────────────────────────────────────────────

    private sealed class Params
    {
        public readonly List<object?> Values = new();
        public string Add(object? value) { Values.Add(value ?? DBNull.Value); return "@p" + (Values.Count - 1); }
    }

    private async Task<object?> ScalarAsync(string sql, Params p, CancellationToken ct)
    {
        object? result = null;
        await using var cmd = CreateCommand(sql, p);
        var (conn, opened) = await OpenAsync(ct);
        try { result = await cmd.ExecuteScalarAsync(ct); }
        finally { if (opened) await conn.CloseAsync(); }
        return result;
    }

    private async Task ReadAsync(string sql, Params p, CancellationToken ct, Action<DbDataReader> onRow)
    {
        await using var cmd = CreateCommand(sql, p);
        var (conn, opened) = await OpenAsync(ct);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) onRow(reader);
        }
        finally { if (opened) await conn.CloseAsync(); }
    }

    private DbCommand CreateCommand(string sql, Params p)
    {
        var conn = _db.Database.GetDbConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < p.Values.Count; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = "p" + i;
            param.Value = p.Values[i] ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        return cmd;
    }

    private async Task<(DbConnection conn, bool opened)> OpenAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State == ConnectionState.Open) return (conn, false);
        await conn.OpenAsync(ct);
        return (conn, true);
    }

    // ── Parsing / conversion ─────────────────────────────────────────────────

    private ResolvedObject Resolve(string? code)
        => (code is not null ? _catalog.Resolve(code) : null)
           ?? throw Invalid("object", $"Unknown or non-discoverable object '{code}'.");

    private static List<WidgetFilterSpec> Combine(IEnumerable<WidgetFilterSpec>? a, IEnumerable<WidgetFilterSpec>? b)
    {
        var list = new List<WidgetFilterSpec>();
        if (a is not null) list.AddRange(a);
        if (b is not null) list.AddRange(b);
        return list;
    }

    public static WidgetQuerySpec? ParseSpec(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration)) return null;
        try
        {
            var spec = JsonSerializer.Deserialize<WidgetQuerySpec>(configuration, Json);
            return string.IsNullOrWhiteSpace(spec?.ObjectCode) ? null : spec;
        }
        catch { return null; }
    }

    private static AggKind ParseAggregation(string? agg) => (agg ?? "Count").ToLowerInvariant() switch
    {
        "count" => AggKind.Count,
        "sum" => AggKind.Sum,
        "average" or "avg" => AggKind.Average,
        "min" => AggKind.Min,
        "max" => AggKind.Max,
        "distinctcount" or "distinct" => AggKind.DistinctCount,
        "percentage" or "percent" => AggKind.Percentage,
        _ => throw Invalid("aggregation", $"Unsupported aggregation '{agg}'."),
    };

    private static bool IsDate(ResolvedField f) => f.Kind is FieldKind.Date or FieldKind.DateTime;

    private static readonly string[] DrilldownTop =
    {
        "NameAr", "Name", "NameEn", "FirstNameAr", "FirstName", "LastNameAr", "LastName", "FullName",
        "TitleAr", "Title", "TitleEn", "EmployeeNumber", "Number", "Code", "Email", "Status", "Priority", "CreatedAt",
    };
    private static int DrilldownPriority(string code)
    {
        var idx = Array.FindIndex(DrilldownTop, t => string.Equals(t, code, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 100;
    }

    private static object? ConvertValue(string? raw, Type target, ResolvedField field)
    {
        if (raw is null) return DBNull.Value;
        target = Nullable.GetUnderlyingType(target) ?? target;
        try
        {
            if (target.IsEnum) return int.TryParse(raw, out var ev) ? ev : Convert.ToInt32(Enum.Parse(target, raw, true));
            if (target == typeof(Guid)) return Guid.Parse(raw);
            if (target == typeof(bool)) return raw is "1" or "true" or "True" || bool.TryParse(raw, out var b) && b;
            if (target == typeof(int) || target == typeof(short) || target == typeof(byte)) return int.Parse(raw);
            if (target == typeof(long)) return long.Parse(raw);
            if (target == typeof(decimal)) return decimal.Parse(raw);
            if (target == typeof(double) || target == typeof(float)) return double.Parse(raw);
            if (target == typeof(DateTime) || target == typeof(DateTimeOffset))
            {
                var dt = DateTime.Parse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            if (target == typeof(DateOnly)) return DateOnly.Parse(raw);
            return raw;
        }
        catch { throw Invalid("filter", $"Invalid value '{raw}' for field '{field.Code}'."); }
    }

    private static object? FormatCell(object? val, string type, Dictionary<int, string>? options)
    {
        if (val is null or DBNull) return null;
        if (options is not null && val is int or short or long && int.TryParse(val.ToString(), out var iv) && options.TryGetValue(iv, out var lbl))
            return lbl;
        return val switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            bool b => b,
            decimal or double or float or int or long or short => Convert.ToDouble(val),
            _ => val.ToString(),
        };
    }

    private static string FormatDateLabel(DateTime dt, string granularity) => granularity.ToLowerInvariant() switch
    {
        "day" => dt.ToString("yyyy-MM-dd"),
        "week" => dt.ToString("yyyy-MM-dd"),
        "month" => dt.ToString("yyyy-MM"),
        "quarter" => $"{dt.Year}-Q{(dt.Month - 1) / 3 + 1}",
        "year" => dt.ToString("yyyy"),
        _ => dt.ToString("yyyy-MM"),
    };

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
