using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceOrderTargets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false, collation: "C"),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    FeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    AssetTypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Alias = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    ActivityIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ConflictLevel = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceOrderTargets_ServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_FeatureId",
                table: "ServiceOrderTargets",
                column: "FeatureId",
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_OrganizationId",
                table: "ServiceOrderTargets",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_ServiceOrderId_FeatureId",
                table: "ServiceOrderTargets",
                columns: new[] { "ServiceOrderId", "FeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_ServiceOrderId_Status",
                table: "ServiceOrderTargets",
                columns: new[] { "ServiceOrderId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceOrderTargets");
        }
    }
}
