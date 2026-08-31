using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
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
                columns: new[] { "Id", "AllowedGeometryType", "attributes_schema", "Color", "DefaultLayerId", "IconUrl", "IsBuiltIn", "Name" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000004"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000001"), "", true, "Poste" },
                    { new Guid("00000000-0000-0000-0000-000000000005"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000002"), "", true, "Transformador" },
                    { new Guid("00000000-0000-0000-0000-000000000006"), 1, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000003"), "", true, "Línea de baja tensión" },
                    { new Guid("00000000-0000-0000-0000-000000000007"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000004"), "", true, "Punto de descarga de agua" },
                    { new Guid("00000000-0000-0000-0000-000000000008"), 0, null, "#3388ff", new Guid("00000000-0000-0000-0001-000000000005"), "", true, "Interruptor" }
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
