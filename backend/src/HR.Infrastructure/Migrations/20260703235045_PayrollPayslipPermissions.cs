using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollPayslipPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Description", "Module", "Name" },
                values: new object[,]
                {
                    { new Guid("53a09ade-1476-8353-80b6-2d593f30c9c1"), "View permission for Payroll.Payslip", "Payroll.Payslip", "View" },
                    { new Guid("74cccb75-09b7-39de-8ca3-27226a54c031"), "Download permission for Payroll.Payslip", "Payroll.Payslip", "Download" },
                    { new Guid("8d0d76f1-7c23-e595-d420-78ae36b4524a"), "Print permission for Payroll.Payslip", "Payroll.Payslip", "Print" }
                });

            // The one-time BackfillSystemRolePermissions has already run, so grant the new Payslip
            // permissions to every system role now (mirroring GrantPayrollCreateFromRunToSystemRoles),
            // otherwise even admins get 403 on the payslip endpoints. Idempotent.
            migrationBuilder.Sql(@"
                INSERT INTO role_permissions (""Id"", ""RoleId"", ""PermissionId"")
                SELECT gen_random_uuid(), r.""Id"", p.""Id""
                FROM roles r
                CROSS JOIN permissions p
                WHERE r.""IsSystemRole"" = true
                  AND p.""Module"" = 'Payroll.Payslip'
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
                DELETE FROM role_permissions rp
                USING permissions p
                WHERE rp.""PermissionId"" = p.""Id"" AND p.""Module"" = 'Payroll.Payslip';
            ");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("53a09ade-1476-8353-80b6-2d593f30c9c1"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("74cccb75-09b7-39de-8ca3-27226a54c031"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: new Guid("8d0d76f1-7c23-e595-d420-78ae36b4524a"));
        }
    }
}
