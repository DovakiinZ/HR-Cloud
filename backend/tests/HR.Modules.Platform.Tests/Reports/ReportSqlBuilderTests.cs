using FluentAssertions;
using FluentValidation;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportSqlBuilderTests
{
    private static ResolvedField Field(string code, string col, FieldKind kind = FieldKind.Text) =>
        new() { Code = code, ColumnName = col, Kind = kind, ClrType = kind == FieldKind.Number ? typeof(int) : typeof(string) };

    private static ResolvedObject Employee() => new()
    {
        Code = "Employee", TableName = "Employees", HasTenant = true, HasSoftDelete = true, KeyColumn = "Id",
        Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase)
        {
            ["FullName"] = Field("FullName", "FullName"),
            ["Salary"] = Field("Salary", "BasicSalary", FieldKind.Number),
            ["DepartmentId"] = Field("DepartmentId", "DepartmentId", FieldKind.Guid),
        }
    };

    private static ResolvedObject Department(bool tenantScoped) => new()
    {
        Code = "Department", TableName = "Departments", HasTenant = tenantScoped, HasSoftDelete = tenantScoped, KeyColumn = "Id",
        Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase) { ["Name"] = Field("Name", "Name") }
    };

    private static ReportQueryModel BaseModel() => new()
    {
        Primary = Employee(), PrimaryAlias = "t0",
        Columns = { new ReportColumnModel { TableAlias = "t0", Field = Employee().Fields["FullName"], OutputCode = "c0" } },
    };

    private static ReportQueryModel ModelWithJoin(ResolvedObject target, string joinType)
    {
        var m = BaseModel();
        m.Joins.Add(new ReportJoinModel
        {
            Alias = "t1", Target = target, SourceAlias = "t0",
            SourceColumn = "DepartmentId", TargetKeyColumn = "Id", JoinType = joinType,
        });
        m.Columns.Add(new ReportColumnModel { TableAlias = "t1", Field = target.Fields["Name"], OutputCode = "c1" });
        return m;
    }

    [Fact]
    public void Selects_columns_with_tenant_and_softdelete_scope()
    {
        var (sql, ps) = ReportSqlBuilder.Build(BaseModel(), Guid.NewGuid(), rowCap: 100);
        sql.Should().Contain("SELECT t0.\"FullName\" AS \"c0\"");
        sql.Should().Contain("FROM \"Employees\" t0");
        sql.Should().Contain("t0.\"TenantId\" = @p0");
        sql.Should().Contain("t0.\"IsDeleted\" = false");
        sql.Should().Contain("LIMIT 101");   // rowCap + 1
        ps.Should().HaveCount(1);            // tenant id
    }

    [Fact]
    public void Emits_inner_join_on_validated_fk()
    {
        var dept = new ResolvedObject { Code = "Department", TableName = "Departments", KeyColumn = "Id",
            Fields = new Dictionary<string, ResolvedField>(StringComparer.OrdinalIgnoreCase) { ["Name"] = Field("Name", "Name") } };
        var m = BaseModel();
        m.Joins.Add(new ReportJoinModel { Alias = "t1", Target = dept, SourceAlias = "t0", SourceColumn = "DepartmentId", TargetKeyColumn = "Id", JoinType = "Left" });
        m.Columns.Add(new ReportColumnModel { TableAlias = "t1", Field = dept.Fields["Name"], OutputCode = "c1" });

        var (sql, _) = ReportSqlBuilder.Build(m, Guid.NewGuid(), 100);
        sql.Should().Contain("LEFT JOIN \"Departments\" t1 ON t1.\"Id\" = t0.\"DepartmentId\"");
        sql.Should().Contain("t1.\"Name\" AS \"c1\"");
    }

    [Fact]
    public void Binds_filter_values_as_parameters()
    {
        var m = BaseModel();
        m.Filters.Add(new ReportFilterModel { TableAlias = "t0", Field = Employee().Fields["Salary"], Operator = ReportFilterOperator.GreaterThan, Value = "5000" });
        var (sql, ps) = ReportSqlBuilder.Build(m, Guid.NewGuid(), 100);
        sql.Should().Contain("t0.\"BasicSalary\" > @p1");
        ps[1].Should().Be(5000);   // converted to the field CLR type
    }

    [Fact]
    public void Orders_by_sort_fields()
    {
        var m = BaseModel();
        m.Sorts.Add(new ReportSortModel { TableAlias = "t0", Field = Employee().Fields["Salary"], Direction = SortDirection.Descending });
        var (sql, _) = ReportSqlBuilder.Build(m, Guid.NewGuid(), 100);
        sql.Should().Contain("ORDER BY t0.\"BasicSalary\" DESC");
    }

    [Fact]
    public void Non_numeric_value_on_numeric_field_throws_ValidationException()
    {
        var m = BaseModel();
        m.Filters.Add(new ReportFilterModel
        {
            TableAlias = "t0",
            Field = Employee().Fields["Salary"],  // FieldKind.Number, ClrType = int
            Operator = ReportFilterOperator.GreaterThan,
            Value = "abc"   // non-numeric → should throw, not silently pass raw string to DB
        });

        FluentActions.Invoking(() => ReportSqlBuilder.Build(m, Guid.NewGuid(), rowCap: 100))
            .Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainSingle(e =>
                e.PropertyName == "filter" && e.ErrorMessage.Contains("abc") && e.ErrorMessage.Contains("Salary"));
    }

    // --- Tenant isolation on joined tables ---------------------------------------------------
    // Scope predicates for a joined table MUST be emitted, or a report joining any tenant-scoped
    // object reads every tenant's rows. They must live in the ON clause: a joined table's predicate
    // in WHERE degrades a LEFT JOIN to an INNER JOIN (the null row fails the predicate).

    [Fact]
    public void Inner_join_to_tenant_scoped_target_is_tenant_and_softdelete_filtered()
    {
        var tenant = Guid.NewGuid();
        var (sql, ps) = ReportSqlBuilder.Build(ModelWithJoin(Department(tenantScoped: true), "Inner"), tenant, 100);

        sql.Should().Contain("INNER JOIN \"Departments\" t1 ON t1.\"Id\" = t0.\"DepartmentId\"" +
                             " AND t1.\"TenantId\" = @p0 AND t1.\"IsDeleted\" = false");
        ps[0].Should().Be(tenant);
    }

    [Fact]
    public void Left_join_scope_predicates_go_in_the_on_clause_and_preserve_the_outer_join()
    {
        var (sql, _) = ReportSqlBuilder.Build(ModelWithJoin(Department(tenantScoped: true), "Left"), Guid.NewGuid(), 100);

        sql.Should().Contain("LEFT JOIN \"Departments\" t1 ON t1.\"Id\" = t0.\"DepartmentId\"" +
                             " AND t1.\"TenantId\" = @p0 AND t1.\"IsDeleted\" = false");

        // The joined table's predicates must not leak into WHERE, which would silently make this inner.
        var where = sql[sql.IndexOf(" WHERE ", StringComparison.Ordinal)..];
        where.Should().NotContain("t1.");
    }

    [Fact]
    public void Join_to_a_non_tenant_scoped_target_gets_no_scope_predicate()
    {
        var (sql, _) = ReportSqlBuilder.Build(ModelWithJoin(Department(tenantScoped: false), "Inner"), Guid.NewGuid(), 100);

        sql.Should().Contain("INNER JOIN \"Departments\" t1 ON t1.\"Id\" = t0.\"DepartmentId\"");
        sql.Should().NotContain("t1.\"TenantId\"");
        sql.Should().NotContain("t1.\"IsDeleted\"");
    }
}
