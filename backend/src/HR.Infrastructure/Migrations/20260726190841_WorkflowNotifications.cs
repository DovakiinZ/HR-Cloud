using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_notification_dispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<int>(type: "integer", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_workflow_notification_dispatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_notification_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestTypeCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Event = table.Column<int>(type: "integer", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: true),
                    RecipientsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubjectAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubjectEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "text", nullable: false),
                    BodyEn = table.Column<string>(type: "text", nullable: false),
                    ChannelBell = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelEmail = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystemOwned = table.Column<bool>(type: "boolean", nullable: false),
                    SystemKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsCustomized = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_workflow_notification_rules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_dispatches_RequestInstanceId_Event_St~",
                table: "workflow_notification_dispatches",
                columns: new[] { "RequestInstanceId", "Event", "StepOrder", "RuleId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_rules_TenantId_RequestTypeCode_Event_~",
                table: "workflow_notification_rules",
                columns: new[] { "TenantId", "RequestTypeCode", "Event", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_rules_TenantId_StepOrder",
                table: "workflow_notification_rules",
                columns: new[] { "TenantId", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_notification_rules_TenantId_SystemKey",
                table: "workflow_notification_rules",
                columns: new[] { "TenantId", "SystemKey" },
                unique: true,
                filter: "\"SystemKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_notification_dispatches");

            migrationBuilder.DropTable(
                name: "workflow_notification_rules");
        }
    }
}
