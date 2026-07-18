using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Reports.Registry;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

// ── Fakes / factories ───────────────────────────────────────────────────────────

file sealed class FakeIds : IReportObjectIdResolver
{
    private readonly Dictionary<string, Guid> _map;
    public FakeIds(params (string code, Guid id)[] entries)
        => _map = entries.ToDictionary(e => e.code, e => e.id, StringComparer.OrdinalIgnoreCase);
    public Guid? ResolveId(string objectCode)
        => _map.TryGetValue(objectCode, out var id) ? id : (Guid?)null;
}

file static class D
{
    public static ReportJoinStep Step(string src, string tgt, string joinField) => new(src, tgt, joinField);

    public static ReportFieldDescriptor Field(
        string key, Guid objId, string objCode, string prop,
        ReportJoinStep[]? join = null, bool aggregatable = false, string? defaultAgg = null,
        bool isDefault = true, string dataType = "Text")
        => new(
            Key: key, LabelAr: "ع", LabelEn: "en",
            Subject: key.Split('.')[0], Group: "grp", DataType: dataType,
            ObjectDefinitionId: objId, ObjectCode: objCode, PropertyPath: prop,
            JoinPath: join ?? Array.Empty<ReportJoinStep>(),
            AllowedOperators: new[] { "Equals", "Between" },
            Filterable: true, Sortable: true, Groupable: true, Aggregatable: aggregatable,
            DefaultAggregation: aggregatable ? (defaultAgg ?? "Sum") : null,
            IsDefault: isDefault, DisplayOrder: 0, FormatPattern: null,
            RequiredPermission: "Employees.View");

    public static IReadOnlyDictionary<string, ReportFieldDescriptor> Map(params ReportFieldDescriptor[] fs)
        => fs.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
}

// ── Tests ───────────────────────────────────────────────────────────────────────

public class QuickReportComposerTests
{
    private static readonly Guid Emp = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid();
    private static readonly Guid Job = Guid.NewGuid();
    private static readonly Guid Att = Guid.NewGuid();

    private static QuickReportPlan Compose(
        IReadOnlyDictionary<string, ReportFieldDescriptor> byKey,
        IReadOnlyList<string> displayKeys,
        IReportObjectIdResolver ids,
        IReadOnlyList<QuickFilterInput>? filters = null,
        IReadOnlyList<string>? groupBy = null,
        IReadOnlyList<QuickSortInput>? sorts = null)
        => QuickReportComposer.Compose(byKey, displayKeys,
            filters ?? Array.Empty<QuickFilterInput>(),
            groupBy ?? Array.Empty<string>(),
            sorts ?? Array.Empty<QuickSortInput>(),
            ids);

    [Fact]
    public void Own_fields_produce_primary_and_no_joins()
    {
        var byKey = D.Map(
            D.Field("employees.employeeNumber", Emp, "Employee", "EmployeeNumber"),
            D.Field("employees.hireDate", Emp, "Employee", "HireDate", dataType: "Date"));
        var ids = new FakeIds(("Employee", Emp));

        var plan = Compose(byKey, new[] { "employees.employeeNumber", "employees.hireDate" }, ids);

        plan.PrimaryObjectId.Should().Be(Emp);
        plan.PrimaryObjectCode.Should().Be("Employee");
        plan.Fields.Should().HaveCount(2);
        plan.Fields.Select(f => f.FieldCode).Should().ContainInOrder("EmployeeNumber", "HireDate");
        plan.Joins.Should().BeEmpty();
        plan.SkippedFieldKeys.Should().BeEmpty();
    }

    [Fact]
    public void Related_field_adds_an_automatic_left_join()
    {
        var byKey = D.Map(
            D.Field("employees.employeeNumber", Emp, "Employee", "EmployeeNumber"),
            D.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { D.Step("Employee", "Department", "DepartmentId") }));
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));

        var plan = Compose(byKey, new[] { "employees.employeeNumber", "employees.departmentName" }, ids);

        plan.PrimaryObjectId.Should().Be(Emp);
        plan.Joins.Should().HaveCount(1);
        plan.Joins[0].SourceObjectId.Should().Be(Emp);
        plan.Joins[0].TargetObjectId.Should().Be(Dept);
        plan.Joins[0].JoinField.Should().Be("DepartmentId");

        var deptField = plan.Fields.Single(f => f.FieldCode == "NameAr");
        deptField.ObjectDefinitionId.Should().Be(Dept, "the joined column resolves against the target object");
    }

    [Fact]
    public void Multi_hop_join_path_is_ordered_from_primary_outward()
    {
        // attendance primary = AttendanceRecord; department reached via Employee.
        var byKey = D.Map(
            D.Field("attendance.checkIn", Att, "AttendanceRecord", "CheckIn", dataType: "DateTime"),
            D.Field("attendance.departmentName", Dept, "Department", "NameAr",
                join: new[]
                {
                    D.Step("AttendanceRecord", "Employee", "EmployeeId"),
                    D.Step("Employee", "Department", "DepartmentId"),
                }));
        var ids = new FakeIds(("AttendanceRecord", Att), ("Employee", Emp), ("Department", Dept));

        var plan = Compose(byKey, new[] { "attendance.checkIn", "attendance.departmentName" }, ids);

        plan.PrimaryObjectId.Should().Be(Att);
        plan.Joins.Should().HaveCount(2);
        plan.Joins[0].SourceObjectId.Should().Be(Att);
        plan.Joins[0].TargetObjectId.Should().Be(Emp);
        plan.Joins[0].SortOrder.Should().Be(0);
        plan.Joins[1].SourceObjectId.Should().Be(Emp);
        plan.Joins[1].TargetObjectId.Should().Be(Dept);
        plan.Joins[1].SortOrder.Should().Be(1);
    }

    [Fact]
    public void Colliding_physical_column_is_skipped_and_reported()
    {
        // departmentName and jobTitleName both map to column "NameAr" — the engine would reject the
        // duplicate output code, so the composer keeps the first and skips the second.
        var byKey = D.Map(
            D.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { D.Step("Employee", "Department", "DepartmentId") }),
            D.Field("employees.jobTitleName", Job, "JobTitle", "NameAr",
                join: new[] { D.Step("Employee", "JobTitle", "JobTitleId") }));
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept), ("JobTitle", Job));

        var plan = Compose(byKey, new[] { "employees.departmentName", "employees.jobTitleName" }, ids);

        plan.Fields.Should().HaveCount(1);
        plan.SkippedFieldKeys.Should().ContainSingle().Which.Should().Be("employees.jobTitleName");
        plan.Joins.Should().HaveCount(1, "the skipped field's join is not added");
        plan.Joins[0].TargetObjectId.Should().Be(Dept);
    }

    [Fact]
    public void Filter_on_own_field_is_kept_and_on_joined_field_is_skipped()
    {
        var byKey = D.Map(
            D.Field("employees.status", Emp, "Employee", "Status"),
            D.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { D.Step("Employee", "Department", "DepartmentId") }));
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));

        var plan = Compose(byKey,
            new[] { "employees.status", "employees.departmentName" }, ids,
            filters: new[]
            {
                new QuickFilterInput("employees.status", "Equals", "Active", null, false),
                new QuickFilterInput("employees.departmentName", "Equals", "HR", null, false),
            });

        plan.Filters.Should().ContainSingle();
        plan.Filters[0].FieldCode.Should().Be("Status");
        plan.Filters[0].Operator.Should().Be(ReportFilterOperator.Equals);
        plan.SkippedFilterKeys.Should().ContainSingle().Which.Should().Be("employees.departmentName");
    }

    [Fact]
    public void Grouping_aggregates_measures_and_marks_summary()
    {
        var byKey = D.Map(
            D.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { D.Step("Employee", "Department", "DepartmentId") }),
            D.Field("employees.basicSalary", Emp, "Employee", "BasicSalary",
                aggregatable: true, dataType: "Currency"));
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));

        var plan = Compose(byKey,
            new[] { "employees.departmentName", "employees.basicSalary" }, ids,
            groupBy: new[] { "employees.departmentName" });

        plan.HasAggregation.Should().BeTrue();
        plan.Groupings.Should().ContainSingle().Which.FieldCode.Should().Be("NameAr");
        plan.Fields.Single(f => f.FieldCode == "BasicSalary").Aggregation.Should().Be(AggregationType.Sum);
        plan.Fields.Single(f => f.FieldCode == "NameAr").Aggregation.Should().BeNull("dimensions are not aggregated");
    }

    [Fact]
    public void Sort_is_kept_only_for_primary_object_fields()
    {
        var byKey = D.Map(
            D.Field("employees.hireDate", Emp, "Employee", "HireDate", dataType: "Date"),
            D.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { D.Step("Employee", "Department", "DepartmentId") }));
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));

        var plan = Compose(byKey,
            new[] { "employees.hireDate", "employees.departmentName" }, ids,
            sorts: new[]
            {
                new QuickSortInput("employees.hireDate", "Descending"),
                new QuickSortInput("employees.departmentName", "Ascending"),
            });

        plan.Sortings.Should().ContainSingle();
        plan.Sortings[0].FieldCode.Should().Be("HireDate");
        plan.Sortings[0].Direction.Should().Be(SortDirection.Descending);
    }

    [Fact]
    public void No_resolvable_fields_throws_validation()
    {
        var byKey = D.Map(D.Field("employees.hireDate", Emp, "Employee", "HireDate"));
        var ids = new FakeIds(("Employee", Emp));

        var act = () => Compose(byKey, new[] { "employees.unknown", "employees.alsoUnknown" }, ids);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Primary_is_derived_from_a_related_fields_source_when_it_leads()
    {
        // Only a related field is selected → primary is the source of its join chain.
        var byKey = D.Map(
            D.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { D.Step("Employee", "Department", "DepartmentId") }));
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));

        var plan = Compose(byKey, new[] { "employees.departmentName" }, ids);

        plan.PrimaryObjectId.Should().Be(Emp);
        plan.PrimaryObjectCode.Should().Be("Employee");
        plan.Joins.Should().ContainSingle();
    }
}
