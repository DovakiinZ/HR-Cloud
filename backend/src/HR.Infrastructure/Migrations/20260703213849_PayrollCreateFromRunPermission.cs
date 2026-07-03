using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollCreateFromRunPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Description", "Module", "Name" },
                values: new object[] { new Guid("15f80127-6240-afe3-5c85-0bd941bf9c68"), "CreateFromRun permission for Payroll.Transaction", "Payroll.Transaction", "CreateFromRun" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("15f80127-6240-afe3-5c85-0bd941bf9c68"));
        }
    }
}
