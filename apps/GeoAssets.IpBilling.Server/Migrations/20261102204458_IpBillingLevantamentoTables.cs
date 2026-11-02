using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.IpBilling.Server.Migrations
{
    /// <inheritdoc />
    public partial class IpBillingLevantamentoTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Levantamento24hLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    WattageW = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Kwh = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Levantamento24hLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LevantamentoHeaders",
                columns: table => new
                {
                    ServiceOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MunicipioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Competencia = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    IsValidated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevantamentoHeaders", x => x.ServiceOrderId);
                });

            migrationBuilder.CreateTable(
                name: "LevantamentoTransformerLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodigoInstalacao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransformerFeatureId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsNewTransformer = table.Column<bool>(type: "boolean", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: true),
                    NormalKwh = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RetroativoKwh = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevantamentoTransformerLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LevantamentoLampDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransformerLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    WattageW = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevantamentoLampDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevantamentoLampDetails_LevantamentoTransformerLines_Transf~",
                        column: x => x.TransformerLineId,
                        principalTable: "LevantamentoTransformerLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Levantamento24hLines_ServiceOrderId",
                table: "Levantamento24hLines",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_LevantamentoHeaders_MunicipioId_Competencia",
                table: "LevantamentoHeaders",
                columns: new[] { "MunicipioId", "Competencia" });

            migrationBuilder.CreateIndex(
                name: "IX_LevantamentoLampDetails_TransformerLineId",
                table: "LevantamentoLampDetails",
                column: "TransformerLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LevantamentoTransformerLines_ServiceOrderId_CodigoInstalacao",
                table: "LevantamentoTransformerLines",
                columns: new[] { "ServiceOrderId", "CodigoInstalacao" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FaturamentoPostings_Municipios_MunicipioId",
                table: "FaturamentoPostings",
                column: "MunicipioId",
                principalTable: "Municipios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FaturamentoPostings_Municipios_MunicipioId",
                table: "FaturamentoPostings");

            migrationBuilder.DropTable(
                name: "Levantamento24hLines");

            migrationBuilder.DropTable(
                name: "LevantamentoHeaders");

            migrationBuilder.DropTable(
                name: "LevantamentoLampDetails");

            migrationBuilder.DropTable(
                name: "LevantamentoTransformerLines");
        }
    }
}
