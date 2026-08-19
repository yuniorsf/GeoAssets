using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameAzureObjectIdToExternalObjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AzureObjectId",
                table: "Users",
                newName: "ExternalObjectId");

            migrationBuilder.RenameIndex(
                name: "IX_Users_AzureObjectId",
                table: "Users",
                newName: "IX_Users_ExternalObjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExternalObjectId",
                table: "Users",
                newName: "AzureObjectId");

            migrationBuilder.RenameIndex(
                name: "IX_Users_ExternalObjectId",
                table: "Users",
                newName: "IX_Users_AzureObjectId");
        }
    }
}
