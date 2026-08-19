using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "geo_entity",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "asset_type",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_OrganizationId",
                table: "geo_entity",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_type_OrganizationId",
                table: "asset_type",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_geo_entity_OrganizationId",
                table: "geo_entity");

            migrationBuilder.DropIndex(
                name: "IX_asset_type_OrganizationId",
                table: "asset_type");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "geo_entity");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "asset_type");
        }
    }
}
