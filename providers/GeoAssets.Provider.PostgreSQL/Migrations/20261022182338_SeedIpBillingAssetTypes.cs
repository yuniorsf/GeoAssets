using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <summary>
    /// Seeds the 3 built-in <c>asset_type</c> rows added for public-lighting (IP) billing
    /// (XD01-176, Phase 0 of the GEOS_FAT_IP epic): Transformador, Poste, and LampGroup24h.
    /// Same global/built-in/protected convention as <c>SeedDomainAssetTypesAndLayers</c>'s 5
    /// rows and <c>AssetType.Point</c>/<c>Line</c>/<c>Area</c> — <c>OrganizationId = Guid.Empty</c>
    /// (unowned, visible to every organization) and <c>IsProtected = true</c> (undeletable,
    /// since Phase 1+ feature/topology data depends on these IDs existing). The 3 IDs
    /// (<c>...0009</c>/<c>...000a</c>/<c>...000b</c>) continue <c>SeedDomainAssetTypesAndLayers</c>'s
    /// numbering (<c>...0004</c> through <c>...0008</c>) and match
    /// <c>GeoAssets.IpBilling.AssetTypes.IpBillingAssetTypes</c>'s own constants exactly —
    /// keep the two in sync if either changes.
    /// </summary>
    public partial class SeedIpBillingAssetTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "asset_type",
                columns: new[] { "Id", "AllowedGeometryType", "attributes_schema", "Color", "DefaultLayerId", "IconUrl", "IsBuiltIn", "IsProtected", "Name", "OrganizationId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000009"), 0, null, "#e67e22", null, "", true, true, "Transformador (IP)", Guid.Empty },
                    { new Guid("00000000-0000-0000-0000-00000000000a"), 0, null, "#8b5a2b", null, "", true, true, "Poste (IP)", Guid.Empty },
                    { new Guid("00000000-0000-0000-0000-00000000000b"), 0, null, "#f1c40f", null, "", true, true, "LampGroup24h (IP)", Guid.Empty }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-00000000000a"));

            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-00000000000b"));
        }
    }
}
