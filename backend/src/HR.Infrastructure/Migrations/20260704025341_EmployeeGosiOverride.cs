using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeGosiOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GOSI is on by default — existing employees keep it enabled.
            migrationBuilder.AddColumn<bool>(
                name: "GosiEnabled",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GosiRateOverride",
                table: "employees",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GosiEnabled",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "GosiRateOverride",
                table: "employees");
        }
    }
}
