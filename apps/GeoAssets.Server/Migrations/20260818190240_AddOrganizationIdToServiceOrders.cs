using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationIdToServiceOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "ServiceOrders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_OrganizationId",
                table: "ServiceOrders",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceOrders_OrganizationId",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "ServiceOrders");
        }
    }
}
