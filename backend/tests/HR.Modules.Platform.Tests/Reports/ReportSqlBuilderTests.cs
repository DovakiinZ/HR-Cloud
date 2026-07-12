using FluentAssertions;
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

    private static ReportQueryModel BaseModel() => new()
    {
        Primary = Employee(), PrimaryAlias = "t0",
        Columns = { new ReportColumnModel { TableAlias = "t0", Field = Employee().Fields["FullName"], OutputCode = "c0" } },
    };

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
}
