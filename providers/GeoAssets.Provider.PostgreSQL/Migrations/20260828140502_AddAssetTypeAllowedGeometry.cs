using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTypeAllowedGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllowedGeometryType",
                table: "asset_type",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "AllowedGeometryType",
                value: null);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "AllowedGeometryType",
                value: null);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "AllowedGeometryType",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedGeometryType",
                table: "asset_type");
        }
    }
}
