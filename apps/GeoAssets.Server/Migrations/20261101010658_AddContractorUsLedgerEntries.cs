using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddContractorUsLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractorUsLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    UsCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PhotoProofJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorUsLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractorUsLedgerEntries_OrganizationId",
                table: "ContractorUsLedgerEntries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorUsLedgerEntries_ServiceOrderId",
                table: "ContractorUsLedgerEntries",
                column: "ServiceOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractorUsLedgerEntries");
        }
    }
}
