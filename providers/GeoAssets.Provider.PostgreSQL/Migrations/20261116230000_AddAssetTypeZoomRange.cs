using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTypeZoomRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Optional per-type zoom range (XD01-311): the map draws a type only between these
            // zoom levels, null leaving a bound open. Both columns are nullable with no default,
            // so every existing and seeded row keeps an open range and renders as before.
            //
            // No Row-Level Security change: asset_type's tenant_isolation_policy
            // (AddAssetTypeRowLevelSecurity) is row-scoped on "OrganizationId" and applies to
            // every column of the row, the new ones included; no column-level grant exists on
            // this table either.
            migrationBuilder.AddColumn<int>(
                name: "MinZoom",
                table: "asset_type",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxZoom",
                table: "asset_type",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinZoom",
                table: "asset_type");

            migrationBuilder.DropColumn(
                name: "MaxZoom",
                table: "asset_type");
        }
    }
}
