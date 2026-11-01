using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <inheritdoc />
    public partial class AddPavingMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_icerp_paving_measurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ChildServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LengthMeters = table.Column<double>(type: "double precision", nullable: false),
                    WidthMeters = table.Column<double>(type: "double precision", nullable: false),
                    DepthMeters = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAreaSquareMeters = table.Column<double>(type: "double precision", nullable: false),
                    ActualAreaSquareMeters = table.Column<double>(type: "double precision", nullable: false),
                    VolumeCubicMeters = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_icerp_paving_measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_field_icerp_paving_measurements_ServiceOrders_Child",
                        column: x => x.ChildServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_field_icerp_paving_measurements_ServiceOrders_Parent",
                        column: x => x.ParentServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_field_icerp_paving_measurements_ChildServiceOrderId",
                table: "field_icerp_paving_measurements",
                column: "ChildServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_field_icerp_paving_measurements_OrganizationId",
                table: "field_icerp_paving_measurements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_field_icerp_paving_measurements_ParentServiceOrderId",
                table: "field_icerp_paving_measurements",
                column: "ParentServiceOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_icerp_paving_measurements");
        }
    }
}
