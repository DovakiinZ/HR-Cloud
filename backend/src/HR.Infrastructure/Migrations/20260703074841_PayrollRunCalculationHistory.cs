using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollRunCalculationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engine_payroll_run_calculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationVersion = table.Column<int>(type: "integer", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CalculatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayrollEngineVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayrollDefinitionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeCount = table.Column<int>(type: "integer", nullable: false),
                    IncludedEmployees = table.Column<int>(type: "integer", nullable: false),
                    ExcludedEmployees = table.Column<int>(type: "integer", nullable: false),
                    TransactionCountConsumed = table.Column<int>(type: "integer", nullable: false),
                    ValidationSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FindingSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GrossTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DeductionTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NetTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    TriggerSource = table.Column<int>(type: "integer", nullable: false),
                    PreviousCalculationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
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
                    table.PrimaryKey("PK_engine_payroll_run_calculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_engine_payroll_run_calculations_engine_payroll_runs_Payroll~",
                        column: x => x.PayrollRunId,
                        principalTable: "engine_payroll_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "engine_payroll_calculation_exclusions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunCalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_engine_payroll_calculation_exclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_engine_payroll_calculation_exclusions_engine_payroll_run_ca~",
                        column: x => x.PayrollRunCalculationId,
                        principalTable: "engine_payroll_run_calculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "engine_payroll_calculation_findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunCalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SuggestedAction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TargetModule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TargetScreen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RelatedEntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_engine_payroll_calculation_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_engine_payroll_calculation_findings_engine_payroll_run_calc~",
                        column: x => x.PayrollRunCalculationId,
                        principalTable: "engine_payroll_run_calculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_exclusions_EmployeeId",
                table: "engine_payroll_calculation_exclusions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_exclusions_PayrollRunCalculatio~1",
                table: "engine_payroll_calculation_exclusions",
                columns: new[] { "PayrollRunCalculationId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_exclusions_PayrollRunCalculation~",
                table: "engine_payroll_calculation_exclusions",
                column: "PayrollRunCalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_exclusions_TenantId",
                table: "engine_payroll_calculation_exclusions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_findings_EmployeeId",
                table: "engine_payroll_calculation_findings",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_findings_PayrollRunCalculationId",
                table: "engine_payroll_calculation_findings",
                column: "PayrollRunCalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_findings_PayrollRunCalculationId~",
                table: "engine_payroll_calculation_findings",
                columns: new[] { "PayrollRunCalculationId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_calculation_findings_TenantId",
                table: "engine_payroll_calculation_findings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_run_calculations_PayrollRunId",
                table: "engine_payroll_run_calculations",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_run_calculations_PayrollRunId_CalculationVer~",
                table: "engine_payroll_run_calculations",
                columns: new[] { "PayrollRunId", "CalculationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_run_calculations_PreviousCalculationId",
                table: "engine_payroll_run_calculations",
                column: "PreviousCalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_engine_payroll_run_calculations_TenantId",
                table: "engine_payroll_run_calculations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engine_payroll_calculation_exclusions");

            migrationBuilder.DropTable(
                name: "engine_payroll_calculation_findings");

            migrationBuilder.DropTable(
                name: "engine_payroll_run_calculations");
        }
    }
}
