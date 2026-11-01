using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddManholeAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_manhole_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConduitFeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoreOccupancyPercentage = table.Column<double>(type: "double precision", nullable: false),
                    SubDuctsJson = table.Column<string>(type: "text", nullable: false),
                    SpliceEnclosureCondition = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Nr33ChecklistAttributesJson = table.Column<string>(type: "text", nullable: false),
                    AuditedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_manhole_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_field_manhole_audits_ConduitFeatureId",
                table: "field_manhole_audits",
                column: "ConduitFeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_field_manhole_audits_OrganizationId",
                table: "field_manhole_audits",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_manhole_audits");
        }
    }
}
