using FluentAssertions;
using HR.Modules.Platform.Services.Catalog;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// Pure SQL-shape tests for reference (FK) display resolution. <see cref="ReportSqlBuilder"/> is a
/// static function over a resolved plan, so these need no database — which matters here, because the
/// DB-backed execution tests skip without REPORTS_TEST_DB and would otherwise leave this path untested.
/// The behaviour under test: a FK column must render the principal's human label, not a raw GUID.
/// </summary>
public class ReportSqlBuilderDisplayTests
{
    private static ResolvedField Plain(string code, string col, FieldKind kind = FieldKind.Number)
        => new() { Code = code, ColumnName = col, ClrType = typeof(int), Kind = kind };

    private static ResolvedField Fk(string code, string col, ResolvedReference r)
        => new() { Code = code, ColumnName = col, ClrType = typeof(Guid), Kind = FieldKind.Reference, Reference = r };

    private static ReportQueryModel Model(params ReportColumnModel[] cols)
    {
        var q = new ReportQueryModel
        {
            Primary = new ResolvedObject
            {
                Code = "LeaveBalance", TableName = "LeaveBalances",
                HasTenant = true, HasSoftDelete = true, KeyColumn = "Id",
            },
            PrimaryAlias = "t0",
        };
        q.Columns.AddRange(cols);
        return q;
    }

    [Fact]
    public void Single_display_column_reference_selects_the_label_not_the_guid()
    {
        var leaveType = new ResolvedReference
        {
            PrincipalTable = "MasterDataItems", PrincipalKeyColumn = "Id", DisplayColumn = "NameAr",
        };
        var model = Model(new ReportColumnModel
        {
            TableAlias = "t0", OutputCode = "LeaveTypeId",
            Field = Fk("LeaveTypeId", "LeaveTypeId", leaveType),
        });

        var (sql, _) = ReportSqlBuilder.Build(model, Guid.NewGuid(), 5000);

        sql.Should().Contain("d0.\"NameAr\"::text AS \"LeaveTypeId\"");
        sql.Should().Contain("LEFT JOIN \"MasterDataItems\" d0 ON d0.\"Id\" = t0.\"LeaveTypeId\"");
        // The bare FK column must NOT be projected — that is the GUID the user saw.
        sql.Should().NotContain("t0.\"LeaveTypeId\" AS");
    }

    [Fact]
    public void Person_reference_concatenates_name_parts()
    {
        var employee = new ResolvedReference
        {
            PrincipalTable = "Employees", PrincipalKeyColumn = "Id",
            DisplayConcatColumns = new[] { "FirstNameAr", "LastNameAr" },
        };
        var model = Model(new ReportColumnModel
        {
            TableAlias = "t0", OutputCode = "EmployeeId",
            Field = Fk("EmployeeId", "EmployeeId", employee),
        });

        var (sql, _) = ReportSqlBuilder.Build(model, Guid.NewGuid(), 5000);

        sql.Should().Contain("concat_ws(' ', d0.\"FirstNameAr\", d0.\"LastNameAr\") AS \"EmployeeId\"");
    }

    [Fact]
    public void Reference_without_a_display_column_falls_back_to_the_key_as_text()
    {
        var opaque = new ResolvedReference { PrincipalTable = "Widgets", PrincipalKeyColumn = "Id" };
        var model = Model(new ReportColumnModel
        {
            TableAlias = "t0", OutputCode = "WidgetId", Field = Fk("WidgetId", "WidgetId", opaque),
        });

        var (sql, _) = ReportSqlBuilder.Build(model, Guid.NewGuid(), 5000);

        sql.Should().Contain("d0.\"Id\"::text AS \"WidgetId\"");
    }

    [Fact]
    public void Each_reference_gets_its_own_display_alias_and_plain_columns_are_untouched()
    {
        var employee = new ResolvedReference
        {
            PrincipalTable = "Employees", PrincipalKeyColumn = "Id", DisplayColumn = "FullName",
        };
        var leaveType = new ResolvedReference
        {
            PrincipalTable = "MasterDataItems", PrincipalKeyColumn = "Id", DisplayColumn = "NameAr",
        };
        var model = Model(
            new ReportColumnModel { TableAlias = "t0", OutputCode = "EmployeeId", Field = Fk("EmployeeId", "EmployeeId", employee) },
            new ReportColumnModel { TableAlias = "t0", OutputCode = "Year", Field = Plain("Year", "Year") },
            new ReportColumnModel { TableAlias = "t0", OutputCode = "LeaveTypeId", Field = Fk("LeaveTypeId", "LeaveTypeId", leaveType) });

        var (sql, _) = ReportSqlBuilder.Build(model, Guid.NewGuid(), 5000);

        sql.Should().Contain("d0.\"FullName\"::text AS \"EmployeeId\"");
        sql.Should().Contain("d1.\"NameAr\"::text AS \"LeaveTypeId\"");
        sql.Should().Contain("t0.\"Year\" AS \"Year\"");
        sql.Should().Contain("LEFT JOIN \"Employees\" d0");
        sql.Should().Contain("LEFT JOIN \"MasterDataItems\" d1");
    }

    [Fact]
    public void Display_joins_do_not_disturb_tenant_scoping_or_parameter_binding()
    {
        var tenantId = Guid.NewGuid();
        var leaveType = new ResolvedReference
        {
            PrincipalTable = "MasterDataItems", PrincipalKeyColumn = "Id", DisplayColumn = "NameAr",
        };
        var model = Model(new ReportColumnModel
        {
            TableAlias = "t0", OutputCode = "LeaveTypeId", Field = Fk("LeaveTypeId", "LeaveTypeId", leaveType),
        });

        var (sql, ps) = ReportSqlBuilder.Build(model, tenantId, 5000);

        // The primary's tenant predicate still binds @p0 to the tenant; a display join adds no parameters,
        // so positional names cannot drift out of step with the values list.
        sql.Should().Contain("WHERE t0.\"TenantId\" = @p0");
        sql.Should().Contain("t0.\"IsDeleted\" = false");
        ps.Should().ContainSingle().Which.Should().Be(tenantId);
    }
}
