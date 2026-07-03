using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollTransactionOriginAndCreatedFromRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedFromRunId",
                table: "engine_payroll_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "engine_payroll_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_transactions_CreatedFromRunId",
                table: "engine_payroll_transactions",
                column: "CreatedFromRunId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_transactions_Origin",
                table: "engine_payroll_transactions",
                column: "Origin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_engine_payroll_transactions_CreatedFromRunId",
                table: "engine_payroll_transactions");

            migrationBuilder.DropIndex(
                name: "IX_engine_payroll_transactions_Origin",
                table: "engine_payroll_transactions");

            migrationBuilder.DropColumn(
                name: "CreatedFromRunId",
                table: "engine_payroll_transactions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "engine_payroll_transactions");
        }
    }
}
