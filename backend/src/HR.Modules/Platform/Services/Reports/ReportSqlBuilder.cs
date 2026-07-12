using System.Globalization;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Catalog;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure SQL construction for a resolved report plan. Identifiers are already validated
/// upstream (every alias/column comes from a ResolvedObject/ResolvedField); values are bound as
/// parameters. Emits LIMIT rowCap+1 so the caller can detect truncation.</summary>
public static class ReportSqlBuilder
{
    public static (string Sql, IReadOnlyList<object?> Parameters) Build(ReportQueryModel model, Guid tenantId, int rowCap)
    {
        var ps = new List<object?>();
        string P(object? v) { ps.Add(v ?? DBNull.Value); return "@p" + (ps.Count - 1); }

        // SELECT
        var select = string.Join(", ", model.Columns.Select(c => $"{c.TableAlias}.{Q(c.Field.ColumnName)} AS {Q(c.OutputCode)}"));
        if (string.IsNullOrEmpty(select)) select = $"{model.PrimaryAlias}.{Q(model.Primary.KeyColumn)}";

        // FROM + JOINs
        var sb = new StringBuilder();
        sb.Append("SELECT ").Append(select)
          .Append(" FROM ").Append(TableRef(model.Primary)).Append(' ').Append(model.PrimaryAlias);
        foreach (var j in model.Joins)
        {
            var kw = j.JoinType?.ToLowerInvariant() switch { "left" => "LEFT JOIN", "right" => "RIGHT JOIN", _ => "INNER JOIN" };
            sb.Append(' ').Append(kw).Append(' ').Append(TableRef(j.Target)).Append(' ').Append(j.Alias)
              .Append(" ON ").Append(j.Alias).Append('.').Append(Q(j.TargetKeyColumn))
              .Append(" = ").Append(j.SourceAlias).Append('.').Append(Q(j.SourceColumn));
        }

        // WHERE: tenant + soft-delete (primary) then filters
        var where = new List<string>();
        if (model.Primary.HasTenant) where.Add($"{model.PrimaryAlias}.{Q("TenantId")} = {P(tenantId)}");
        if (model.Primary.HasSoftDelete) where.Add($"{model.PrimaryAlias}.{Q("IsDeleted")} = false");
        foreach (var f in model.Filters) AppendFilter(where, f, P);
        if (where.Count > 0) sb.Append(" WHERE ").Append(string.Join(" AND ", where));

        // ORDER BY
        if (model.Sorts.Count > 0)
            sb.Append(" ORDER BY ").Append(string.Join(", ", model.Sorts.Select(s =>
                $"{s.TableAlias}.{Q(s.Field.ColumnName)} {(s.Direction == SortDirection.Descending ? "DESC" : "ASC")}")));

        sb.Append(" LIMIT ").Append(rowCap + 1);
        return (sb.ToString(), ps);
    }

    private static void AppendFilter(List<string> where, ReportFilterModel f, Func<object?, string> P)
    {
        var col = $"{f.TableAlias}.{Q(f.Field.ColumnName)}";
        switch (f.Operator)
        {
            case ReportFilterOperator.IsNull: where.Add($"{col} IS NULL"); break;
            case ReportFilterOperator.IsNotNull: where.Add($"{col} IS NOT NULL"); break;
            case ReportFilterOperator.Contains: where.Add($"{col}::text ILIKE '%' || {P(f.Value ?? "")} || '%'"); break;
            case ReportFilterOperator.StartsWith: where.Add($"{col}::text ILIKE {P((f.Value ?? "") + "%")}"); break;
            case ReportFilterOperator.EndsWith: where.Add($"{col}::text ILIKE {P("%" + (f.Value ?? ""))}"); break;
            case ReportFilterOperator.NotEquals: where.Add($"{col} <> {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.GreaterThan: where.Add($"{col} > {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.LessThan: where.Add($"{col} < {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.GreaterThanOrEqual: where.Add($"{col} >= {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.LessThanOrEqual: where.Add($"{col} <= {P(Convert(f.Value, f.Field))}"); break;
            case ReportFilterOperator.Between:
                where.Add($"{col} BETWEEN {P(Convert(f.Value, f.Field))} AND {P(Convert(f.ValueTo, f.Field))}"); break;
            case ReportFilterOperator.In:
                var vals = (f.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (vals.Length > 0) where.Add($"{col} IN ({string.Join(",", vals.Select(v => P(Convert(v, f.Field))))})");
                break;
            case ReportFilterOperator.NotIn:
                var nvals = (f.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (nvals.Length > 0) where.Add($"{col} NOT IN ({string.Join(",", nvals.Select(v => P(Convert(v, f.Field))))})");
                break;
            default: where.Add($"{col} = {P(Convert(f.Value, f.Field))}"); break;
        }
    }

    private static object? Convert(string? raw, ResolvedField field)
    {
        if (raw is null) return DBNull.Value;
        var t = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        try
        {
            if (t.IsEnum) return int.TryParse(raw, out var ev) ? ev : (int)Enum.Parse(t, raw, true);
            if (t == typeof(Guid)) return Guid.Parse(raw);
            if (t == typeof(bool)) return raw is "1" or "true" or "True";
            if (t == typeof(int) || t == typeof(short) || t == typeof(byte)) return int.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(double) || t == typeof(float)) return double.Parse(raw, CultureInfo.InvariantCulture);
            if (t == typeof(DateTime))
                return DateTime.SpecifyKind(DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), DateTimeKind.Utc);
            if (t == typeof(DateOnly)) return DateOnly.Parse(raw, CultureInfo.InvariantCulture);
            return raw;
        }
        catch
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("filter", $"Invalid value '{raw}' for field '{field.Code}'.")
            });
        }
    }

    private static string TableRef(ResolvedObject o) => o.Schema is { Length: > 0 } s ? $"{Q(s)}.{Q(o.TableName)}" : Q(o.TableName);
    private static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
