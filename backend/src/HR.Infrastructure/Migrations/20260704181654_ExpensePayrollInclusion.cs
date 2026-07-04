using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpensePayrollInclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeInPayroll",
                table: "engine_expenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayrollMonth",
                table: "engine_expenses",
                type: "timestamp with time zone",
                nullable: true);

            // Preserve prior behaviour: expenses used to enter payroll implicitly by DecidedAt month.
            // Flag existing Approved expenses into their DecidedAt month so nothing silently drops out.
            migrationBuilder.Sql(@"
                UPDATE engine_expenses
                SET ""IncludeInPayroll"" = TRUE,
                    ""PayrollMonth"" = date_trunc('month', ""DecidedAt"")
                WHERE ""Status"" = 'Approved';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeInPayroll",
                table: "engine_expenses");

            migrationBuilder.DropColumn(
                name: "PayrollMonth",
                table: "engine_expenses");
        }
    }
}
