using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2DeferredEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "engine_request_effect_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "engine_completion_effects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeasedBy",
                table: "engine_completion_effects",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeasedUntil",
                table: "engine_completion_effects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "engine_completion_effects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "engine_completion_effects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledFor",
                table: "engine_completion_effects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "engine_effect_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletionEffectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_engine_effect_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_engine_effect_attempts_engine_completion_effects_Completion~",
                        column: x => x.CompletionEffectId,
                        principalTable: "engine_completion_effects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_engine_completion_effects_IdempotencyKey",
                table: "engine_completion_effects",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engine_completion_effects_Status_ScheduledFor_NextAttemptAt",
                table: "engine_completion_effects",
                columns: new[] { "Status", "ScheduledFor", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_engine_effect_attempts_CompletionEffectId",
                table: "engine_effect_attempts",
                column: "CompletionEffectId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_effect_attempts_TenantId",
                table: "engine_effect_attempts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engine_effect_attempts");

            migrationBuilder.DropIndex(
                name: "IX_engine_completion_effects_IdempotencyKey",
                table: "engine_completion_effects");

            migrationBuilder.DropIndex(
                name: "IX_engine_completion_effects_Status_ScheduledFor_NextAttemptAt",
                table: "engine_completion_effects");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "engine_request_effect_definitions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "engine_completion_effects");

            migrationBuilder.DropColumn(
                name: "LeasedBy",
                table: "engine_completion_effects");

            migrationBuilder.DropColumn(
                name: "LeasedUntil",
                table: "engine_completion_effects");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "engine_completion_effects");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "engine_completion_effects");

            migrationBuilder.DropColumn(
                name: "ScheduledFor",
                table: "engine_completion_effects");
        }
    }
}
