using System.Collections.Concurrent;
using System.Text;
using HR.Domain.Engines.MasterData;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HR.Modules.Platform.Services.Catalog;

/// <summary>
/// Builds the Object/Property Registry catalog from the live EF Core model.
/// Discovery rule (generic, never per-object): an entity is a discoverable business
/// object iff it is mapped to a table and carries a "TenantId" property. Any future
/// entity added to <see cref="ApplicationDbContext"/> therefore appears automatically.
/// </summary>
public sealed class ObjectCatalogService : IObjectCatalogService
{
    private readonly ApplicationDbContext _db;

    // Cached per model instance (the model is effectively a singleton for the app lifetime).
    private static readonly ConcurrentDictionary<IModel, CatalogCache> _cache = new();

    public ObjectCatalogService(ApplicationDbContext db) => _db = db;

    private sealed record CatalogCache(
        IReadOnlyList<CatalogObjectDto> Objects,
        IReadOnlyDictionary<string, ResolvedObject> Resolved);

    private CatalogCache Cache => _cache.GetOrAdd(_db.Model, Build);

    public IReadOnlyList<CatalogObjectDto> GetCatalog() => Cache.Objects;

    public CatalogObjectDto? GetObject(string objectCode) =>
        Cache.Objects.FirstOrDefault(o => string.Equals(o.Code, objectCode, StringComparison.OrdinalIgnoreCase));

    public ResolvedObject? Resolve(string objectCode) =>
        objectCode is not null && Cache.Resolved.TryGetValue(objectCode, out var r) ? r : null;

    // ── Build ─────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> HiddenColumns = new(StringComparer.OrdinalIgnoreCase)
    { "TenantId", "IsDeleted", "DeletedAt", "DeletedBy" };

    private static CatalogCache Build(IModel model)
    {
        var objects = new List<CatalogObjectDto>();
        var resolved = new Dictionary<string, ResolvedObject>(StringComparer.OrdinalIgnoreCase);

        // First pass: index every mapped entity's table + display column (for label resolution),
        // by CLR type (for configured FKs) and by simple name (for convention-based references).
        var displayIndex = new Dictionary<Type, RefTarget>();
        var nameIndex = new Dictionary<string, RefTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var et in model.GetEntityTypes())
        {
            if (et.IsOwned()) continue;
            var table = et.GetTableName();
            if (table is null) continue;
            var so = StoreObjectIdentifier.Table(table, et.GetSchema());
            var keyCol = et.FindPrimaryKey()?.Properties.FirstOrDefault()?.GetColumnName(so) ?? "Id";
            var (display, concat) = PickDisplay(et, so);
            var target = new RefTarget(table, et.GetSchema(), keyCol, display, concat, et.ClrType.Name);
            displayIndex[et.ClrType] = target;
            nameIndex[et.ClrType.Name] = target;
        }

        foreach (var et in model.GetEntityTypes())
        {
            if (et.IsOwned()) continue;
            var table = et.GetTableName();
            if (table is null) continue;
            if (et.FindProperty("TenantId") is null) continue; // ← discovery rule

            var so = StoreObjectIdentifier.Table(table, et.GetSchema());
            var keyCol = et.FindPrimaryKey()?.Properties.FirstOrDefault()?.GetColumnName(so) ?? "Id";
            var hasSoftDelete = et.FindProperty("IsDeleted") is not null;
            var hasCreated = et.FindProperty("CreatedAt") is not null;

            // Map FK column → principal, for reference fields & label joins.
            var fkByColumn = new Dictionary<string, ResolvedReference>(StringComparer.OrdinalIgnoreCase);
            var fkPrincipalCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in et.GetForeignKeys())
            {
                if (fk.Properties.Count != 1) continue;
                var col = fk.Properties[0].GetColumnName(so);
                if (col is null) continue;
                var principal = fk.PrincipalEntityType;
                if (!displayIndex.TryGetValue(principal.ClrType, out var pi)) continue;
                fkByColumn[col] = new ResolvedReference
                {
                    PrincipalTable = pi.table,
                    PrincipalSchema = pi.schema,
                    PrincipalKeyColumn = pi.keyCol,
                    DisplayColumn = pi.display,
                    DisplayConcatColumns = pi.concat,
                };
                fkPrincipalCode[col] = principal.ClrType.Name;
            }

            // Convention-based references: a Guid "<Object>Id" column that has no configured FK
            // is treated as a canonical reference to that object (matches the platform's
            // governance model). This is what makes "group by Department" show names, not GUIDs.
            foreach (var prop in et.GetProperties())
            {
                if (prop.IsShadowProperty()) continue;
                var clr = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                if (clr != typeof(Guid)) continue;
                var col = prop.GetColumnName(so) ?? prop.Name;
                if (HiddenColumns.Contains(col)) continue;
                if (col.Equals(keyCol, StringComparison.OrdinalIgnoreCase)) continue;
                if (fkByColumn.ContainsKey(col)) continue;
                if (!col.EndsWith("Id", StringComparison.Ordinal) || col.Length <= 2) continue;

                var targetName = ConventionTarget(col);
                if (targetName is null) continue;
                // Most lookups are their own entity (DepartmentId → Department). The rest are rows in
                // the shared MasterDataItems table discriminated by ObjectType (LeaveTypeId, JobTitleId,
                // AllowanceTypeId, …) — there is no "LeaveType" CLR type to find, so without this the
                // column stayed an unresolved GUID and reports printed the raw key. Matching on the
                // principal key alone is sufficient: MasterDataItem ids are unique across ObjectTypes,
                // so the discriminator is not needed to resolve one that is already known.
                if (!nameIndex.TryGetValue(targetName, out var t)
                    && !(MasterDataObjectType.All.Contains(targetName, StringComparer.OrdinalIgnoreCase)
                         && nameIndex.TryGetValue(nameof(MasterDataItem), out t)))
                    continue;
                if (string.Equals(t.table, table, StringComparison.OrdinalIgnoreCase) && t.display is null && t.concat is null) continue;
                fkByColumn[col] = new ResolvedReference
                {
                    PrincipalTable = t.table, PrincipalSchema = t.schema, PrincipalKeyColumn = t.keyCol,
                    DisplayColumn = t.display, DisplayConcatColumns = t.concat,
                };
                fkPrincipalCode[col] = t.code;
            }

            var fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase);
            var dtoFields = new List<CatalogFieldDto>();

            foreach (var prop in et.GetProperties())
            {
                if (prop.IsShadowProperty()) continue;
                var col = prop.GetColumnName(so) ?? prop.Name;
                if (HiddenColumns.Contains(col)) continue;

                var clr = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                fkByColumn.TryGetValue(col, out var reference);
                var kind = MapKind(clr, reference is not null);

                fields[col] = new ResolvedField
                {
                    Code = col,
                    ColumnName = col,
                    ClrType = clr,
                    Kind = kind,
                    Reference = reference,
                };

                // Exclude the primary key from the user-facing field list (kept in resolution for COUNT/drilldown).
                var isKey = string.Equals(col, keyCol, StringComparison.OrdinalIgnoreCase);
                if (isKey) continue;

                var isMeasure = kind is FieldKind.Number or FieldKind.Decimal or FieldKind.Currency or FieldKind.Percentage;
                var isDate = kind is FieldKind.Date or FieldKind.DateTime;
                var isGroupable = kind is FieldKind.Text or FieldKind.Boolean or FieldKind.Enum or FieldKind.Reference || isDate;

                List<EnumOptionDto>? options = null;
                if (clr.IsEnum)
                {
                    options = new List<EnumOptionDto>();
                    foreach (var v in Enum.GetValues(clr))
                        options.Add(new EnumOptionDto { Value = Convert.ToInt32(v), Label = Labels.EnumLabel(clr, v!) });
                }

                dtoFields.Add(new CatalogFieldDto
                {
                    Code = col,
                    NameEn = Labels.FieldEn(col, reference is not null),
                    NameAr = Labels.FieldAr(col, reference is not null),
                    FieldType = kind.ToString(),
                    IsMeasure = isMeasure,
                    IsGroupable = isGroupable,
                    IsFilterable = true,
                    IsDate = isDate,
                    IsReference = reference is not null,
                    ReferenceObjectCode = fkPrincipalCode.TryGetValue(col, out var pc) ? pc : null,
                    Options = options,
                });
            }

            var code = et.ClrType.Name;
            resolved[code] = new ResolvedObject
            {
                Code = code,
                TableName = table,
                Schema = et.GetSchema(),
                HasTenant = true,
                HasSoftDelete = hasSoftDelete,
                KeyColumn = keyCol,
                Fields = fields,
            };

            objects.Add(new CatalogObjectDto
            {
                Code = code,
                NameEn = Labels.ObjectEn(code),
                NameAr = Labels.ObjectAr(code),
                Module = ModuleOf(et.ClrType),
                Icon = Labels.ObjectIcon(code),
                HasTenantScope = true,
                HasSoftDelete = hasSoftDelete,
                HasDateCreated = hasCreated,
                FieldCount = dtoFields.Count,
                Fields = dtoFields.OrderBy(f => f.IsReference ? 1 : f.IsMeasure ? 2 : 0).ThenBy(f => f.NameEn).ToList(),
            });
        }

        var ordered = objects.OrderBy(o => o.Module).ThenBy(o => o.NameEn).ToList();
        return new CatalogCache(ordered, resolved);
    }

    private sealed record RefTarget(string table, string? schema, string keyCol, string? display, string[]? concat, string code);

    /// <summary>Map a "<Object>Id" column to a principal object name by convention.</summary>
    private static string? ConventionTarget(string col)
    {
        var stripped = col[..^2]; // drop "Id"
        switch (stripped.ToLowerInvariant())
        {
            case "manager": case "deputymanager": case "directmanager":
            case "linemanager": case "reportsto": case "supervisor":
                return "Employee";
            case "owner": case "createdby": case "updatedby": case "assignee": case "assignedto":
                return "User";
        }
        if (stripped.StartsWith("Parent", StringComparison.Ordinal) && stripped.Length > 6)
            stripped = stripped["Parent".Length..];
        return stripped.Length > 0 ? stripped : null;
    }

    private static FieldKind MapKind(Type clr, bool isReference)
    {
        if (isReference) return FieldKind.Reference;
        if (clr.IsEnum) return FieldKind.Enum;
        if (clr == typeof(bool)) return FieldKind.Boolean;
        if (clr == typeof(Guid)) return FieldKind.Guid;
        if (clr == typeof(DateTime) || clr == typeof(DateTimeOffset)) return FieldKind.DateTime;
        if (clr == typeof(DateOnly)) return FieldKind.Date;
        if (clr == typeof(int) || clr == typeof(long) || clr == typeof(short) || clr == typeof(byte))
            return FieldKind.Number;
        if (clr == typeof(decimal) || clr == typeof(double) || clr == typeof(float))
            return FieldKind.Decimal;
        return FieldKind.Text;
    }

    /// <summary>Choose a human-friendly display column on a principal entity for FK label joins.</summary>
    private static (string? display, string[]? concat) PickDisplay(IEntityType et, StoreObjectIdentifier so)
    {
        string? Col(string name) => et.FindProperty(name)?.GetColumnName(so);

        foreach (var c in new[] { "NameAr", "Name", "NameEn", "TitleAr", "Title", "DisplayName", "FullName" })
            if (Col(c) is { } col) return (col, null);

        // Person-style names → concat first/last (Arabic preferred).
        var fa = Col("FirstNameAr"); var la = Col("LastNameAr");
        if (fa is not null && la is not null) return (null, new[] { fa, la });
        var fn = Col("FirstName"); var ln = Col("LastName");
        if (fn is not null && ln is not null) return (null, new[] { fn, ln });

        foreach (var c in new[] { "Code", "Number", "EmployeeNumber" })
            if (Col(c) is { } col) return (col, null);

        return (null, null);
    }

    private static string ModuleOf(Type clr)
    {
        var ns = clr.Namespace ?? "";
        foreach (var marker in new[] { ".Entities.", ".Engines." })
        {
            var i = ns.IndexOf(marker, StringComparison.Ordinal);
            if (i >= 0)
            {
                var rest = ns[(i + marker.Length)..];
                var seg = rest.Split('.')[0];
                return seg;
            }
        }
        var parts = ns.Split('.');
        return parts.Length > 0 ? parts[^1] : "General";
    }
}
