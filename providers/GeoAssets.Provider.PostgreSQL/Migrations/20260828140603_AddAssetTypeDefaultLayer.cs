using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTypeDefaultLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultLayerId",
                table: "asset_type",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "DefaultLayerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "DefaultLayerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "DefaultLayerId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_asset_type_DefaultLayerId",
                table: "asset_type",
                column: "DefaultLayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_asset_type_layer_DefaultLayerId",
                table: "asset_type",
                column: "DefaultLayerId",
                principalTable: "layer",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_asset_type_layer_DefaultLayerId",
                table: "asset_type");

            migrationBuilder.DropIndex(
                name: "IX_asset_type_DefaultLayerId",
                table: "asset_type");

            migrationBuilder.DropColumn(
                name: "DefaultLayerId",
                table: "asset_type");
        }
    }
}
