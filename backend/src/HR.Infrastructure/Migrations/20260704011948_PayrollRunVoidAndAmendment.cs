using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollRunVoidAndAmendment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AmendsRunId",
                table: "engine_payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByRunId",
                table: "engine_payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "engine_payroll_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "engine_payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                table: "engine_payroll_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Description", "Module", "Name" },
                values: new object[,]
                {
                    { new Guid("339fbc59-4064-38a5-aa1a-57786708699d"), "Amend permission for Payroll.Run", "Payroll.Run", "Amend" },
                    { new Guid("a559e38a-15ba-aac4-182c-6c252eacfc65"), "Void permission for Payroll.Run", "Payroll.Run", "Void" },
                    { new Guid("c203dd1e-f1ff-5af8-8a3d-e6156d7a583a"), "Reissue permission for Payroll.Run", "Payroll.Run", "Reissue" }
                });

            // Grant the new run-lifecycle permissions to every system role (backfill already ran). Idempotent.
            migrationBuilder.Sql(@"
                INSERT INTO role_permissions (""Id"", ""RoleId"", ""PermissionId"")
                SELECT gen_random_uuid(), r.""Id"", p.""Id""
                FROM roles r
                CROSS JOIN permissions p
                WHERE r.""IsSystemRole"" = true
                  AND p.""Module"" = 'Payroll.Run'
                  AND NOT EXISTS (
                      SELECT 1 FROM role_permissions rp
                      WHERE rp.""RoleId"" = r.""Id"" AND rp.""PermissionId"" = p.""Id""
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM role_permissions rp USING permissions p
                WHERE rp.""PermissionId"" = p.""Id"" AND p.""Module"" = 'Payroll.Run';
            ");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("339fbc59-4064-38a5-aa1a-57786708699d"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("a559e38a-15ba-aac4-182c-6c252eacfc65"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("c203dd1e-f1ff-5af8-8a3d-e6156d7a583a"));

            migrationBuilder.DropColumn(
                name: "AmendsRunId",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "SupersededByRunId",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "engine_payroll_runs");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "engine_payroll_runs");
        }
    }
}
