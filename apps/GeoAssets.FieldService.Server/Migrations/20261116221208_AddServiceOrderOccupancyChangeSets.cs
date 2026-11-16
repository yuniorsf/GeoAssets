using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderOccupancyChangeSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceOrderOccupancyChangeSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false, collation: "C"),
                    PoleFeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ChangesJson = table.Column<string>(type: "text", nullable: false),
                    BaselineVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StagedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StagedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderOccupancyChangeSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceOrderOccupancyChangeSets_ServiceOrderTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "ServiceOrderTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderOccupancyChangeSets_OrganizationId",
                table: "ServiceOrderOccupancyChangeSets",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderOccupancyChangeSets_ServiceOrderId_Status",
                table: "ServiceOrderOccupancyChangeSets",
                columns: new[] { "ServiceOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderOccupancyChangeSets_TargetId",
                table: "ServiceOrderOccupancyChangeSets",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderOccupancyChangeSets_TargetId_Staged",
                table: "ServiceOrderOccupancyChangeSets",
                column: "TargetId",
                unique: true,
                filter: "\"Status\" = 'Staged'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceOrderOccupancyChangeSets");
        }
    }
}
