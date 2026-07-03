using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantAttendancePayrollImpactToSystemRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The 2E permission "Attendance.PayrollImpact.Create" was inserted by
            // 20260702003229_AttendancePayrollImpactPermission, but the one-time grant in
            // 20260626234541_BackfillSystemRolePermissions had already run, so no system role holds it
            // and the payroll-impact/sync endpoint returns 403 even for admins. Grant it to every system
            // role, mirroring the backfill pattern. Idempotent.
            migrationBuilder.Sql(@"
                INSERT INTO role_permissions (""Id"", ""RoleId"", ""PermissionId"")
                SELECT gen_random_uuid(), r.""Id"", p.""Id""
                FROM roles r
                CROSS JOIN permissions p
                WHERE r.""IsSystemRole"" = true
                  AND p.""Module"" = 'Attendance.PayrollImpact'
                  AND p.""Name"" = 'Create'
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
                USING roles r, permissions p
                WHERE rp.""RoleId"" = r.""Id"" AND rp.""PermissionId"" = p.""Id""
                  AND r.""IsSystemRole"" = true
                  AND p.""Module"" = 'Attendance.PayrollImpact'
                  AND p.""Name"" = 'Create';
            ");
        }
    }
}
