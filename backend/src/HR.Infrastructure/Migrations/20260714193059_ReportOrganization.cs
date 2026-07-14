using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "engine_report_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "engine_report_definition_tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportTagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engine_report_definition_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "engine_report_folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_engine_report_folders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "engine_report_tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_engine_report_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "engine_report_user_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    LastViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_engine_report_user_states", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_engine_report_definition_tags_ReportDefinitionId_ReportTagId",
                table: "engine_report_definition_tags",
                columns: new[] { "ReportDefinitionId", "ReportTagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engine_report_folders_TenantId_ParentFolderId",
                table: "engine_report_folders",
                columns: new[] { "TenantId", "ParentFolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_engine_report_tags_TenantId_Name",
                table: "engine_report_tags",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engine_report_user_states_TenantId_UserId_ReportDefinitionId",
                table: "engine_report_user_states",
                columns: new[] { "TenantId", "UserId", "ReportDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engine_report_definition_tags");

            migrationBuilder.DropTable(
                name: "engine_report_folders");

            migrationBuilder.DropTable(
                name: "engine_report_tags");

            migrationBuilder.DropTable(
                name: "engine_report_user_states");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "engine_report_definitions");
        }
    }
}
