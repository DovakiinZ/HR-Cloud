using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttendancePermissionsSchemaAndMonthlyCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExcusedMinutes",
                table: "engine_attendance_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionCapMode",
                table: "attendance_policies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionMaxMinutesPerMonth",
                table: "attendance_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PermissionMaxPerMonth",
                table: "attendance_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "attendance_permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FromMinutes = table.Column<int>(type: "integer", nullable: false),
                    ToMinutes = table.Column<int>(type: "integer", nullable: false),
                    ExcusedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_permissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_permissions_TenantId",
                table: "attendance_permissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_permissions_TenantId_EmployeeId_Date",
                table: "attendance_permissions",
                columns: new[] { "TenantId", "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_permissions_TenantId_RequestInstanceId",
                table: "attendance_permissions",
                columns: new[] { "TenantId", "RequestInstanceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_permissions");

            migrationBuilder.DropColumn(
                name: "ExcusedMinutes",
                table: "engine_attendance_records");

            migrationBuilder.DropColumn(
                name: "PermissionCapMode",
                table: "attendance_policies");

            migrationBuilder.DropColumn(
                name: "PermissionMaxMinutesPerMonth",
                table: "attendance_policies");

            migrationBuilder.DropColumn(
                name: "PermissionMaxPerMonth",
                table: "attendance_policies");
        }
    }
}
