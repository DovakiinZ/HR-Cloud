using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttendancePermissionUnpaidBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidDailyPayableHours",
                table: "attendance_policies",
                type: "numeric",
                nullable: false,
                defaultValue: 8m);

            migrationBuilder.AddColumn<int>(
                name: "UnpaidDivisorBasis",
                table: "attendance_policies",
                type: "integer",
                nullable: false,
                defaultValue: 2); // DayBasis.Fixed30

            migrationBuilder.AddColumn<string>(
                name: "UnpaidWageComponentCodes",
                table: "attendance_policies",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnpaidDailyPayableHours",
                table: "attendance_policies");

            migrationBuilder.DropColumn(
                name: "UnpaidDivisorBasis",
                table: "attendance_policies");

            migrationBuilder.DropColumn(
                name: "UnpaidWageComponentCodes",
                table: "attendance_policies");
        }
    }
}
