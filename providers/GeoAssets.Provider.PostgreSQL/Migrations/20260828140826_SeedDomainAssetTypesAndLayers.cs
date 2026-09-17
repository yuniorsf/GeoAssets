using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <summary>
    /// Seeds 5 <c>layer</c> rows (no organization ownership — <c>Layer</c> doesn't implement
    /// <c>IOrgOwnedResource</c>) and 5 <c>asset_type</c> rows. The <c>asset_type</c> rows are
    /// explicitly seeded with <c>OrganizationId = Guid.Empty</c> — the documented "no
    /// organization assigned" sentinel (see <c>IOrgOwnedResource</c>, <c>AssetType.OrganizationId</c>)
    /// that marks these as global built-in defaults, same as <c>AssetType.Point</c>/<c>Line</c>/<c>Area</c>.
    /// <c>OrgResourceAuthorizationHandler</c> already treats that sentinel as unowned and always
    /// passes the org check for it, and no asset-type query in this codebase filters by
    /// <c>OrganizationId</c> today, so these rows are visible to every organization.
    /// </summary>
    public partial class SeedDomainAssetTypesAndLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "layer",
                columns: new[] { "Id", "Color", "DashArray", "FillColor", "FillOpacity", "GeometryType", "IconUrl", "Name", "Radius", "Weight" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0001-000000000001"), "#8b5a2b", null, "#3388ff", 0.20000000000000001, 0, "", "Poste", 6.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-000000000002"), "#e67e22", null, "#3388ff", 0.20000000000000001, 0, "", "Transformador", 8.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-000000000003"), "#f1c40f", null, "#3388ff", 0.20000000000000001, 1, "", "Línea de baja tensión", 8.0, 2.0 },
                    { new Guid("00000000-0000-0000-0001-000000000004"), "#3498db", null, "#3388ff", 0.20000000000000001, 0, "", "Punto de descarga de agua", 6.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-000000000005"), "#e74c3c", null, "#3388ff", 0.20000000000000001, 0, "", "Interruptor", 7.0, 3.0 }
                });

            migrationBuilder.InsertData(
                table: "asset_type",
                columns: new[] { "Id", "AllowedGeometryType", "attributes_schema", "Color", "DefaultLayerId", "IconUrl", "IsBuiltIn", "Name", "OrganizationId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000004"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000001"), "", true, "Poste", Guid.Empty },
                    { new Guid("00000000-0000-0000-0000-000000000005"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000002"), "", true, "Transformador", Guid.Empty },
                    { new Guid("00000000-0000-0000-0000-000000000006"), 1, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000003"), "", true, "Línea de baja tensión", Guid.Empty },
                    { new Guid("00000000-0000-0000-0000-000000000007"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000004"), "", true, "Punto de descarga de agua", Guid.Empty },
                    { new Guid("00000000-0000-0000-0000-000000000008"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000005"), "", true, "Interruptor", Guid.Empty }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000001"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000002"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000003"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000004"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000005"));
        }
    }
}
