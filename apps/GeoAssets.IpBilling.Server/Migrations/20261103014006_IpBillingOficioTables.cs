using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.IpBilling.Server.Migrations
{
    /// <inheritdoc />
    public partial class IpBillingOficioTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OficioHeaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MunicipioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Competencia = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    NumeroPrefeitura = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsValidated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OficioHeaders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OficioLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodigoInstalacao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Agregado = table.Column<int>(type: "integer", nullable: false),
                    Retroativo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OficioLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OficioOperationGroupDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationGroup = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OficioOperationGroupDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OficioOperationGroupDetails_OficioLines_LineId",
                        column: x => x.LineId,
                        principalTable: "OficioLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OficioHeaders_MunicipioId_Competencia",
                table: "OficioHeaders",
                columns: new[] { "MunicipioId", "Competencia" });

            migrationBuilder.CreateIndex(
                name: "IX_OficioHeaders_MunicipioId_NumeroPrefeitura",
                table: "OficioHeaders",
                columns: new[] { "MunicipioId", "NumeroPrefeitura" },
                unique: true,
                filter: "\"NumeroPrefeitura\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OficioHeaders_ServiceOrderId",
                table: "OficioHeaders",
                column: "ServiceOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OficioLines_ServiceOrderId_CodigoInstalacao",
                table: "OficioLines",
                columns: new[] { "ServiceOrderId", "CodigoInstalacao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OficioOperationGroupDetails_LineId",
                table: "OficioOperationGroupDetails",
                column: "LineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OficioHeaders");

            migrationBuilder.DropTable(
                name: "OficioOperationGroupDetails");

            migrationBuilder.DropTable(
                name: "OficioLines");
        }
    }
}
