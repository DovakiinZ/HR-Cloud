using FluentAssertions;
using HR.Application.Reports.Registry;
using HR.Application.SemanticCatalog;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

// ────────────────────────────────────────────────────────────────────────────────
//  Fakes
// ────────────────────────────────────────────────────────────────────────────────

file sealed class FakeIdResolver : IReportObjectIdResolver
{
    private readonly Dictionary<string, Guid> _map;
    public FakeIdResolver(params (string code, Guid id)[] entries)
        => _map = entries.ToDictionary(e => e.code, e => e.id, StringComparer.OrdinalIgnoreCase);
    public Guid? ResolveId(string objectCode)
        => _map.TryGetValue(objectCode, out var id) ? id : null;
}

file sealed class FakeCatalog : IObjectCatalogService
{
    private readonly Dictionary<string, CatalogObjectDto> _objects;
    public FakeCatalog(params CatalogObjectDto[] objects)
        => _objects = objects.ToDictionary(o => o.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CatalogObjectDto> GetCatalog() => _objects.Values.ToList();
    public CatalogObjectDto? GetObject(string objectCode)
        => _objects.TryGetValue(objectCode, out var o) ? o : null;
    public ResolvedObject? Resolve(string objectCode) => null; // not needed by adapter
}

file sealed class FakeSemanticProvider : ISemanticCatalogProvider
{
    private readonly IReadOnlyList<SemanticDomain> _domains;
    private readonly IReadOnlyList<SemanticObject> _objects;

    public FakeSemanticProvider(IReadOnlyList<SemanticDomain> domains, IReadOnlyList<SemanticObject> objects)
    {
        _domains = domains;
        _objects = objects;
    }

    public IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext ctx) => _domains;
    public IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext ctx, string? domainCode = null)
        => domainCode is null ? _objects : _objects.Where(o => o.DomainCode == domainCode).ToList();
    public SemanticObject? GetObject(CatalogQueryContext ctx, string objectCode)
        => _objects.FirstOrDefault(o => o.ObjectCode == objectCode);
    public IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext ctx, string? domainCode = null)
        => Array.Empty<SemanticMetric>();
    public SemanticMetric? GetMetric(CatalogQueryContext ctx, string metricCode) => null;
    public IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext ctx, string query)
        => Array.Empty<SemanticSearchHit>();
    public CatalogHealth GetHealth()
        => new CatalogHealth(0, 0, 0, 0, Array.Empty<HiddenItem>());
}

// ────────────────────────────────────────────────────────────────────────────────
//  Test helpers
// ────────────────────────────────────────────────────────────────────────────────

file static class Factories
{
    public static SemanticField OwnField(string objectCode, string fieldCode,
        string nameAr = "اسم", string nameEn = "Name", string groupCode = "grp",
        bool reportEnabled = true, SemanticFieldRole role = SemanticFieldRole.Dimension)
        => new SemanticField(objectCode, fieldCode, nameAr, nameEn, "", "", groupCode, null,
            Array.Empty<string>(), role, true, reportEnabled);

    public static SemanticField RefField(string objectCode, string fieldCode,
        string nameAr = "اسم", string nameEn = "Name", string groupCode = "grp",
        bool reportEnabled = true)
        => new SemanticField(objectCode, fieldCode, nameAr, nameEn, "", "", groupCode, null,
            Array.Empty<string>(), SemanticFieldRole.Dimension, true, reportEnabled);

    public static SemanticDomain Domain(string code, string nameAr = "نطاق", string nameEn = "Domain", int sort = 1)
        => new SemanticDomain(code, nameAr, nameEn, "", "", "Icon", sort);

    public static SemanticObject Obj(string objectCode, string domainCode, params SemanticField[] fields)
        => new SemanticObject(objectCode, domainCode, "الاسم", objectCode, "", "", "Icon",
            Array.Empty<string>(), true, Array.Empty<SemanticFieldGroup>(),
            null, Array.Empty<SemanticFilter>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            fields);

    public static CatalogObjectDto CatalogObj(string code, params CatalogFieldDto[] fields)
        => new CatalogObjectDto { Code = code, NameEn = code, NameAr = code, Module = "test", Fields = fields.ToList() };

    public static CatalogFieldDto TextField(string code)
        => new CatalogFieldDto { Code = code, NameEn = code, NameAr = code, FieldType = "Text" };

    public static CatalogFieldDto NumberField(string code)
        => new CatalogFieldDto { Code = code, NameEn = code, NameAr = code, FieldType = "Number", IsMeasure = true };

    public static CatalogFieldDto RefField(string code, string targetCode)
        => new CatalogFieldDto { Code = code, NameEn = code, NameAr = code, FieldType = "Reference", IsReference = true, ReferenceObjectCode = targetCode };

    public static ReportRegistryContext CtxWithPerms(params string[] perms)
        => new ReportRegistryContext(perms);

    public static ReportFieldRegistryAdapter BuildAdapter(
        ISemanticCatalogProvider semantic,
        IObjectCatalogService catalog,
        IReportObjectIdResolver ids)
        => new ReportFieldRegistryAdapter(semantic, catalog, ids,
            NullLogger<ReportFieldRegistryAdapter>.Instance);
}

// ────────────────────────────────────────────────────────────────────────────────
//  Tests
// ────────────────────────────────────────────────────────────────────────────────

public class ReportFieldRegistryAdapterTests
{
    // ── 1. Own field → no join, correct object+column ────────────────────────

    [Fact]
    public void Own_field_maps_to_object_and_column_no_join()
    {
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "HireDate", "تاريخ التوظيف", "Hire Date", "employment"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.TextField("HireDate")));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var fields = registry.GetFields(ctx, "employees");
        fields.Should().HaveCount(1);

        var f = fields[0];
        f.ObjectCode.Should().Be("Employee");
        f.PropertyPath.Should().Be("HireDate");
        f.ObjectDefinitionId.Should().Be(empId);
        f.JoinPath.Should().BeEmpty();
    }

    // ── 2. Reference field → target display col + join path ──────────────────

    [Fact]
    public void Related_field_maps_to_target_display_and_join_path()
    {
        var empId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.RefField("Employee", "DepartmentId", "القسم", "Department", "organization"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.RefField("DepartmentId", "Department")),
            Factories.CatalogObj("Department",
                Factories.TextField("Id"),
                Factories.TextField("NameAr"),
                Factories.TextField("Code")));
        var ids = new FakeIdResolver(("Employee", empId), ("Department", deptId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var fields = registry.GetFields(ctx, "employees");
        fields.Should().HaveCount(1);

        var f = fields[0];
        f.Key.Should().Be("employees.departmentName");
        f.ObjectCode.Should().Be("Department");
        f.ObjectDefinitionId.Should().Be(deptId);
        f.PropertyPath.Should().Be("NameAr"); // highest priority display column
        f.JoinPath.Should().HaveCount(1);
        f.JoinPath[0].SourceObjectCode.Should().Be("Employee");
        f.JoinPath[0].TargetObjectCode.Should().Be("Department");
        f.JoinPath[0].JoinField.Should().Be("DepartmentId");
    }

    // ── 3. Invalid field (column absent from catalog) → excluded + in health ─

    [Fact]
    public void Invalid_field_is_excluded_and_in_health()
    {
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("employees");
        // SemanticField has "NonExistentColumn" but catalog doesn't expose it
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "NonExistentColumn", "مجهول", "Unknown"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee", Factories.TextField("Id")));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var fields = registry.GetFields(ctx, "employees");
        fields.Should().BeEmpty("the field is not on the catalog object");

        var health = registry.GetHealth();
        health.ExcludedFields.Should().Be(1);
        health.Exclusions.Should().HaveCount(1);
        health.Exclusions[0].Reason.Should().Contain("NonExistentColumn");
    }

    // ── 4. Permission filter hides fields without matching permission ─────────

    [Fact]
    public void Permission_filter_hides_payroll_without_permission()
    {
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("payroll");
        var payObj = Factories.Obj("Employee", "payroll",
            Factories.OwnField("Employee", "BasicSalary", "الراتب الأساسي", "Basic Salary"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { payObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee", Factories.NumberField("BasicSalary")));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);

        // Without Payroll.View → empty
        var ctxNoPerms = Factories.CtxWithPerms("Employees.View");
        registry.GetFields(ctxNoPerms, "payroll").Should().BeEmpty();

        // With Payroll.View → visible
        var ctxWithPayroll = Factories.CtxWithPerms("Payroll.View");
        registry.GetFields(ctxWithPayroll, "payroll").Should().HaveCount(1);
    }

    // ── 5. GetHealth counts exclusions regardless of permissions ─────────────

    [Fact]
    public void GetHealth_counts_all_exclusions_ignoring_permissions()
    {
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "GoodField"),
            Factories.OwnField("Employee", "MissingField"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee", Factories.TextField("GoodField")));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var health = registry.GetHealth();

        health.ExcludedFields.Should().Be(1);
        health.VisibleFields.Should().Be(1);
        health.Exclusions.Should().ContainSingle(e => e.Key.Contains("MissingField"));
    }

    // ── 6. Resolve returns matched descriptors + joins + unknown keys ─────────

    [Fact]
    public void Resolve_returns_descriptors_joins_and_unknown_keys()
    {
        var empId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "HireDate", "تاريخ", "Hire Date"),
            Factories.RefField("Employee", "DepartmentId", "القسم", "Department"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.TextField("HireDate"),
                Factories.RefField("DepartmentId", "Department")),
            Factories.CatalogObj("Department",
                Factories.TextField("NameAr")));
        var ids = new FakeIdResolver(("Employee", empId), ("Department", deptId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var result = registry.Resolve(ctx, new[]
        {
            "employees.hireDate",
            "employees.departmentName",
            "employees.unknown_key"
        });

        result.Fields.Should().HaveCount(2);
        result.RequiredJoins.Should().HaveCount(1, "departmentName needs Employee→Department");
        result.UnknownKeys.Should().ContainSingle(k => k == "employees.unknown_key");
    }

    // ── 7. Operators derive from data type ────────────────────────────────────

    [Fact]
    public void Operators_derive_from_datatype()
    {
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "BasicSalary", "الراتب", "Salary", role: SemanticFieldRole.Measure),
            Factories.OwnField("Employee", "HireDate", "تاريخ", "Hire Date"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.NumberField("BasicSalary"),
                new CatalogFieldDto { Code = "HireDate", NameEn = "Hire Date", NameAr = "تاريخ", FieldType = "Date" }));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var salary = registry.GetField(ctx, "employees.basicSalary");
        salary.Should().NotBeNull();
        salary!.AllowedOperators.Should().Contain("Between");
        salary.Aggregatable.Should().BeTrue();

        var hireDate = registry.GetField(ctx, "employees.hireDate");
        hireDate.Should().NotBeNull();
        hireDate!.AllowedOperators.Should().Contain("Between");
        hireDate.AllowedOperators.Should().NotContain("Contains");
    }

    // ── 8. Duplicate key → second excluded ───────────────────────────────────

    [Fact]
    public void Duplicate_key_second_is_excluded()
    {
        // Two semantic objects in same domain both produce "employees.basicSalary"
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("employees");

        // Two objects in the same domain, both producing the same key
        var empObj1 = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "BasicSalary", "الراتب 1", "Salary 1"));
        var empObj2 = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "BasicSalary", "الراتب 2", "Salary 2")); // duplicate

        // We'll build a single object that has two fields with the same derived key pattern
        // by making them identical FieldCode on the same object — second should be excluded
        var objWithDuplicates = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "Salary", "الراتب 1", "Salary 1"),
            Factories.OwnField("Employee", "Salary", "الراتب 2", "Salary 2")); // same code → same key

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { objWithDuplicates });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.NumberField("Salary")));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        // Only one visible field because duplicate key was excluded
        var fields = registry.GetFields(ctx, "employees");
        fields.Should().HaveCount(1);

        var health = registry.GetHealth();
        health.Exclusions.Should().Contain(e => e.Reason.Contains("duplicate"));
    }

    // ── 9. Field with unknown object code → excluded ──────────────────────────

    [Fact]
    public void Field_with_unknown_object_code_is_excluded()
    {
        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "HireDate"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee", Factories.TextField("HireDate")));
        // No id for "Employee" → will be excluded
        var ids = new FakeIdResolver(); // empty

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var fields = registry.GetFields(ctx, "employees");
        fields.Should().BeEmpty();

        var health = registry.GetHealth();
        health.ExcludedFields.Should().Be(1);
        health.Exclusions[0].Reason.Should().Contain("Employee");
    }

    // ── 10. Reference field with missing target → excluded ───────────────────

    [Fact]
    public void Reference_field_with_missing_target_is_excluded()
    {
        var empId = Guid.NewGuid();
        var domain = Factories.Domain("employees");
        var empObj = Factories.Obj("Employee", "employees",
            Factories.RefField("Employee", "DepartmentId", "القسم", "Dept"));

        var semantic = new FakeSemanticProvider(new[] { domain }, new[] { empObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.RefField("DepartmentId", "Department")));
        // Department not in catalog → target missing
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);
        var ctx = Factories.CtxWithPerms("Employees.View");

        var fields = registry.GetFields(ctx, "employees");
        fields.Should().BeEmpty();

        var health = registry.GetHealth();
        health.Exclusions.Should().ContainSingle();
    }

    // ── 11. GetSubjects returns only subjects with ≥1 visible field ───────────

    [Fact]
    public void GetSubjects_returns_only_subjects_with_visible_fields()
    {
        var empId = Guid.NewGuid();
        var domains = new[]
        {
            Factories.Domain("employees", "الموظفون", "Employees", 1),
            Factories.Domain("payroll", "الرواتب", "Payroll", 2),
        };
        var empObj = Factories.Obj("Employee", "employees",
            Factories.OwnField("Employee", "HireDate"));
        var payObj = Factories.Obj("Employee", "payroll",
            Factories.OwnField("Employee", "BasicSalary", "الراتب", "Salary"));

        var semantic = new FakeSemanticProvider(domains, new[] { empObj, payObj });
        var catalog = new FakeCatalog(
            Factories.CatalogObj("Employee",
                Factories.TextField("HireDate"),
                Factories.NumberField("BasicSalary")));
        var ids = new FakeIdResolver(("Employee", empId));

        var registry = Factories.BuildAdapter(semantic, catalog, ids);

        // Without Payroll.View → only employees subject visible
        var ctxNoPayroll = Factories.CtxWithPerms("Employees.View");
        var subjects = registry.GetSubjects(ctxNoPayroll);
        subjects.Should().HaveCount(1);
        subjects[0].Key.Should().Be("employees");

        // With both → both visible
        var ctxBoth = Factories.CtxWithPerms("Employees.View", "Payroll.View");
        registry.GetSubjects(ctxBoth).Should().HaveCount(2);
    }
}
