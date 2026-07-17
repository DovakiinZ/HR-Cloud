using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmailDeliveryAndScheduleTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DayOfMonth",
                table: "engine_report_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DayOfWeek",
                table: "engine_report_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeOfDayMinutes",
                table: "engine_report_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttachmentFileId",
                table: "engine_email_queue",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DayOfMonth",
                table: "engine_report_schedules");

            migrationBuilder.DropColumn(
                name: "DayOfWeek",
                table: "engine_report_schedules");

            migrationBuilder.DropColumn(
                name: "TimeOfDayMinutes",
                table: "engine_report_schedules");

            migrationBuilder.DropColumn(
                name: "AttachmentFileId",
                table: "engine_email_queue");
        }
    }
}
