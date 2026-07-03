using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollRunTargetPeriodAndCalcPointers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentCalculationVersion",
                table: "engine_payroll_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCalculatedAt",
                table: "engine_payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastCalculatedByUserId",
                table: "engine_payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetPeriodMonth",
                table: "engine_payroll_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetPeriodYear",
                table: "engine_payroll_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill existing rows before the unique index is created so duplicate (defId, 0, 0) rows
            // never collide. PeriodStart is always set at creation time and is the authoritative source.
            migrationBuilder.Sql(@"
                UPDATE engine_payroll_runs
                SET ""TargetPeriodYear"" = EXTRACT(YEAR FROM ""PeriodStart"")::int,
                    ""TargetPeriodMonth"" = EXTRACT(MONTH FROM ""PeriodStart"")::int
                WHERE ""TargetPeriodYear"" = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_runs_PayrollDefinitionId_TargetPeriodYear_Ta~",
                table: "engine_payroll_runs",
                columns: new[] { "PayrollDefinitionId", "TargetPeriodYear", "TargetPeriodMonth" },
                unique: true,
                filter: "\"State\" <> 11");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_runs_TargetPeriodYear_TargetPeriodMonth",
                table: "engine_payroll_runs",
                columns: new[] { "TargetPeriodYear", "TargetPeriodMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_engine_payroll_runs_PayrollDefinitionId_TargetPeriodYear_Ta~",
                table: "engine_payroll_runs");

            migrationBuilder.DropIndex(
                name: "IX_engine_payroll_runs_TargetPeriodYear_TargetPeriodMonth",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "CurrentCalculationVersion",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "LastCalculatedAt",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "LastCalculatedByUserId",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "TargetPeriodMonth",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "TargetPeriodYear",
                table: "engine_payroll_runs");
        }
    }
}
