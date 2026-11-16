using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.IpBilling.Server.Migrations
{
    /// <inheritdoc />
    public partial class IpBillingDistribuidoraKwhPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DistribuidoraId",
                table: "Municipios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProvisional",
                table: "FaturamentoPostings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Distribuidoras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Distribuidoras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalizacaoLampDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationGroup = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    WattageW = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalizacaoLampDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalizacaoLampDetails_FiscalizacaoLines_LineId",
                        column: x => x.LineId,
                        principalTable: "FiscalizacaoLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OficioLampDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationGroup = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    WattageW = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OficioLampDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OficioLampDetails_OficioLines_LineId",
                        column: x => x.LineId,
                        principalTable: "OficioLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DistribuidoraLampWattages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistribuidoraId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    WattageW = table.Column<int>(type: "integer", nullable: false),
                    BallastLossW = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistribuidoraLampWattages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistribuidoraLampWattages_Distribuidoras_DistribuidoraId",
                        column: x => x.DistribuidoraId,
                        principalTable: "Distribuidoras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Municipios_DistribuidoraId",
                table: "Municipios",
                column: "DistribuidoraId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoPostings_IsProvisional",
                table: "FaturamentoPostings",
                column: "IsProvisional");

            migrationBuilder.CreateIndex(
                name: "IX_DistribuidoraLampWattages_DistribuidoraId_Code_WattageW",
                table: "DistribuidoraLampWattages",
                columns: new[] { "DistribuidoraId", "Code", "WattageW" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Distribuidoras_Name",
                table: "Distribuidoras",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Distribuidoras_OrganizationId",
                table: "Distribuidoras",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalizacaoLampDetails_LineId",
                table: "FiscalizacaoLampDetails",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_OficioLampDetails_LineId",
                table: "OficioLampDetails",
                column: "LineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Municipios_Distribuidoras_DistribuidoraId",
                table: "Municipios",
                column: "DistribuidoraId",
                principalTable: "Distribuidoras",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Municipios_Distribuidoras_DistribuidoraId",
                table: "Municipios");

            migrationBuilder.DropTable(
                name: "DistribuidoraLampWattages");

            migrationBuilder.DropTable(
                name: "FiscalizacaoLampDetails");

            migrationBuilder.DropTable(
                name: "OficioLampDetails");

            migrationBuilder.DropTable(
                name: "Distribuidoras");

            migrationBuilder.DropIndex(
                name: "IX_Municipios_DistribuidoraId",
                table: "Municipios");

            migrationBuilder.DropIndex(
                name: "IX_FaturamentoPostings_IsProvisional",
                table: "FaturamentoPostings");

            migrationBuilder.DropColumn(
                name: "DistribuidoraId",
                table: "Municipios");

            migrationBuilder.DropColumn(
                name: "IsProvisional",
                table: "FaturamentoPostings");
        }
    }
}
