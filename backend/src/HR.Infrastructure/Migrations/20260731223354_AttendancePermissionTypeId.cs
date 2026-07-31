using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttendancePermissionTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PermissionTypeId",
                table: "attendance_permissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_permissions_TenantId_EmployeeId_PermissionTypeId~",
                table: "attendance_permissions",
                columns: new[] { "TenantId", "EmployeeId", "PermissionTypeId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_attendance_permissions_TenantId_EmployeeId_PermissionTypeId~",
                table: "attendance_permissions");

            migrationBuilder.DropColumn(
                name: "PermissionTypeId",
                table: "attendance_permissions");
        }
    }
}
