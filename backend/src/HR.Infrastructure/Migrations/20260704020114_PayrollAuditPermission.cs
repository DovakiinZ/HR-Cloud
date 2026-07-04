using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollAuditPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Description", "Module", "Name" },
                values: new object[] { new Guid("74e25307-6e7b-5b92-9c1e-d6156b0b9f4b"), "View permission for Payroll.Audit", "Payroll.Audit", "View" });

            // Grant to every system role (backfill already ran). Idempotent.
            migrationBuilder.Sql(@"
                INSERT INTO role_permissions (""Id"", ""RoleId"", ""PermissionId"")
                SELECT gen_random_uuid(), r.""Id"", p.""Id""
                FROM roles r
                CROSS JOIN permissions p
                WHERE r.""IsSystemRole"" = true
                  AND p.""Module"" = 'Payroll.Audit' AND p.""Name"" = 'View'
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
                WHERE rp.""PermissionId"" = p.""Id"" AND p.""Module"" = 'Payroll.Audit';
            ");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("74e25307-6e7b-5b92-9c1e-d6156b0b9f4b"));
        }
    }
}
