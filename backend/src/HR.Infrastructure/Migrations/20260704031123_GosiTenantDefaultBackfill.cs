using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GosiTenantDefaultBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every tenant gets the default 9.75% GOSI rate. Existing profiles left at 0 (unset) get the
            // default so GOSI is deducted by default; per-employee toggle/override still applies.
            migrationBuilder.Sql(@"UPDATE engine_company_profiles SET ""GosiRate"" = 9.75 WHERE ""GosiRate"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
