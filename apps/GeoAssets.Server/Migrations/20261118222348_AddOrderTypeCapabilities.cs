using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <inheritdoc />
    public partial class AddOrderTypeCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapabilitiesJson",
                table: "OrderTypes",
                type: "text",
                nullable: true);

            // XD01-361: occupancy commit becomes an explicit capability instead of "has a target policy". Order types stored
            // before this migration overlay the code-registered ones (OrderTypeRegistry.LoadFromAsync), so the shared-use
            // types — the only ones the code registers with a target policy — keep committing occupancy on approval.
            migrationBuilder.Sql(
                """
                UPDATE "OrderTypes"
                SET "CapabilitiesJson" = '["occupancy-commit"]'
                WHERE "Id" IN ('shared-use-pole-work', 'shared-use-occupant-notice')
                  AND "TargetPolicyJson" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapabilitiesJson",
                table: "OrderTypes");
        }
    }
}
