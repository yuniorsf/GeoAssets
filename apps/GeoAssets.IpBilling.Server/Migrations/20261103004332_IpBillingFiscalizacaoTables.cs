using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.IpBilling.Server.Migrations
{
    /// <inheritdoc />
    public partial class IpBillingFiscalizacaoTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalizacaoHeaders",
                columns: table => new
                {
                    ServiceOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MunicipioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Competencia = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    IsValidated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalizacaoHeaders", x => x.ServiceOrderId);
                });

            migrationBuilder.CreateTable(
                name: "FiscalizacaoLines",
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
                    table.PrimaryKey("PK_FiscalizacaoLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalizacaoOperationGroupDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationGroup = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalizacaoOperationGroupDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalizacaoOperationGroupDetails_FiscalizacaoLines_LineId",
                        column: x => x.LineId,
                        principalTable: "FiscalizacaoLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalizacaoHeaders_MunicipioId_Competencia",
                table: "FiscalizacaoHeaders",
                columns: new[] { "MunicipioId", "Competencia" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalizacaoLines_ServiceOrderId_CodigoInstalacao",
                table: "FiscalizacaoLines",
                columns: new[] { "ServiceOrderId", "CodigoInstalacao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalizacaoOperationGroupDetails_LineId",
                table: "FiscalizacaoOperationGroupDetails",
                column: "LineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalizacaoHeaders");

            migrationBuilder.DropTable(
                name: "FiscalizacaoOperationGroupDetails");

            migrationBuilder.DropTable(
                name: "FiscalizacaoLines");
        }
    }
}
