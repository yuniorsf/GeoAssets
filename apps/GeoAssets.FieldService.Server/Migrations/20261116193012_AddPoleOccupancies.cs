using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPoleOccupancies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_pole_occupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoleFeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_pole_occupancies", x => x.Id);
                    table.UniqueConstraint("AK_field_pole_occupancies_Id_OrganizationId", x => new { x.Id, x.OrganizationId });
                });

            migrationBuilder.CreateTable(
                name: "field_pole_occupancy_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoleOccupancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccupantOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperatorCnpj = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    AnatelLicenseNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CableDiameterMm = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    CableLoadDaN = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    AttachedCableFeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_pole_occupancy_entries", x => x.Id);
                    table.CheckConstraint("CK_field_pole_occupancy_entries_SlotIndex", "\"SlotIndex\" IS NULL OR \"SlotIndex\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_field_pole_occupancy_entries_field_pole_occupancies",
                        columns: x => new { x.PoleOccupancyId, x.OrganizationId },
                        principalTable: "field_pole_occupancies",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_field_pole_occupancies_OrganizationId_PoleFeatureId",
                table: "field_pole_occupancies",
                columns: new[] { "OrganizationId", "PoleFeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_field_pole_occupancies_PoleFeatureId",
                table: "field_pole_occupancies",
                column: "PoleFeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_field_pole_occupancy_entries_OccupantOrganizationId",
                table: "field_pole_occupancy_entries",
                column: "OccupantOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_field_pole_occupancy_entries_OrganizationId",
                table: "field_pole_occupancy_entries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_field_pole_occupancy_entries_PoleOccupancyId_OrganizationId",
                table: "field_pole_occupancy_entries",
                columns: new[] { "PoleOccupancyId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_field_pole_occupancy_entries_PoleOccupancyId_SlotIndex",
                table: "field_pole_occupancy_entries",
                columns: new[] { "PoleOccupancyId", "SlotIndex" },
                unique: true,
                filter: "\"SlotIndex\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_pole_occupancy_entries");

            migrationBuilder.DropTable(
                name: "field_pole_occupancies");
        }
    }
}
