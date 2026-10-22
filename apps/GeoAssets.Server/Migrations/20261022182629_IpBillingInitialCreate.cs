using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <summary>
    /// Creates the IP-billing (GEOS_FAT_IP) reference-data schema (XD01-176, Phase 0) and seeds
    /// the non-user-editable <c>LampTypes</c>/<c>LampWattageOptions</c> catalog, mirroring
    /// <c>GeoAssets.Provider.PostgreSQL</c>'s <c>SeedDomainAssetTypesAndLayers</c> "seed via
    /// migration, not a runtime seeder" convention for closed reference catalogs.
    ///
    /// <c>BallastLossW</c> is nonzero only for the MT/MV/VM/VS rows (the discharge-lamp types
    /// that require an external ballast/reactor), per this ticket's own deliverable description.
    ///
    /// IMPORTANT (flagged in XD01-176's Jira resolution note): no copy of the legacy GEOS_FAT_IP
    /// module's actual wattage/ballast-loss table exists anywhere in this repository or was
    /// supplied with the ticket, so the wattage options and ballast-loss figures below are a
    /// best-effort reconstruction from common Brazilian public-lighting catalogs, not a verified
    /// transcription of the real legacy table. Reconcile against the real legacy data (and adjust
    /// via a follow-up migration if needed) before this feeds a real bill — the parent epic's own
    /// "golden-master" verification strategy (XD01-175) is the intended mechanism for catching
    /// this kind of drift once real legacy workbooks are available.
    /// </summary>
    public partial class IpBillingInitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IpTransformerIndexes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipioIbgeCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CodigoInstalacao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransformerFeatureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpTransformerIndexes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LampTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LampTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Municipios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IbgeCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DistribuidoraOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrefeituraOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrigemLevantamento = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrigemFiscalizacao = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrigemOficio = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrigemMemoriaCalculo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Municipios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LampWattageOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LampTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WattageW = table.Column<int>(type: "integer", nullable: false),
                    BallastLossW = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LampWattageOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LampWattageOptions_LampTypes_LampTypeId",
                        column: x => x.LampTypeId,
                        principalTable: "LampTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Faturamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Competencia = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    KwhFaturado = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faturamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Faturamentos_Municipios_MunicipioId",
                        column: x => x.MunicipioId,
                        principalTable: "Municipios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnidadesOrganizacionais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnidadesOrganizacionais", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnidadesOrganizacionais_Municipios_MunicipioId",
                        column: x => x.MunicipioId,
                        principalTable: "Municipios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_MunicipioId_Competencia",
                table: "Faturamentos",
                columns: new[] { "MunicipioId", "Competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpTransformerIndexes_MunicipioIbgeCode_CodigoInstalacao",
                table: "IpTransformerIndexes",
                columns: new[] { "MunicipioIbgeCode", "CodigoInstalacao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LampTypes_Code",
                table: "LampTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LampWattageOptions_LampTypeId_WattageW",
                table: "LampWattageOptions",
                columns: new[] { "LampTypeId", "WattageW" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Municipios_DistribuidoraOrganizationId",
                table: "Municipios",
                column: "DistribuidoraOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Municipios_IbgeCode",
                table: "Municipios",
                column: "IbgeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Municipios_PrefeituraOrganizationId",
                table: "Municipios",
                column: "PrefeituraOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UnidadesOrganizacionais_MunicipioId",
                table: "UnidadesOrganizacionais",
                column: "MunicipioId");

            // ── Seed data: LampTypes (see this migration's own doc comment re: provenance) ──

            migrationBuilder.InsertData(
                table: "LampTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000001"), "FL", "Fluorescente" },
                    { new Guid("00000000-0000-0000-0002-000000000002"), "HL", "Halógena" },
                    { new Guid("00000000-0000-0000-0002-000000000003"), "IN", "Incandescente" },
                    { new Guid("00000000-0000-0000-0002-000000000004"), "LD", "LED" },
                    { new Guid("00000000-0000-0000-0002-000000000005"), "MT", "Mista" },
                    { new Guid("00000000-0000-0000-0002-000000000006"), "MV", "Multivapor" },
                    { new Guid("00000000-0000-0000-0002-000000000007"), "VM", "Vapor de Mercúrio" },
                    { new Guid("00000000-0000-0000-0002-000000000008"), "VS", "Vapor de Sódio" },
                });

            migrationBuilder.InsertData(
                table: "LampWattageOptions",
                columns: new[] { "Id", "LampTypeId", "WattageW", "BallastLossW" },
                values: new object[,]
                {
                    // FL — Fluorescente (no ballast loss modeled)
                    { new Guid("00000000-0000-0000-0003-000000000001"), new Guid("00000000-0000-0000-0002-000000000001"), 20,  0m },
                    { new Guid("00000000-0000-0000-0003-000000000002"), new Guid("00000000-0000-0000-0002-000000000001"), 40,  0m },
                    { new Guid("00000000-0000-0000-0003-000000000003"), new Guid("00000000-0000-0000-0002-000000000001"), 65,  0m },
                    { new Guid("00000000-0000-0000-0003-000000000004"), new Guid("00000000-0000-0000-0002-000000000001"), 110, 0m },

                    // HL — Halógena (no ballast loss modeled)
                    { new Guid("00000000-0000-0000-0003-000000000010"), new Guid("00000000-0000-0000-0002-000000000002"), 100, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000011"), new Guid("00000000-0000-0000-0002-000000000002"), 150, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000012"), new Guid("00000000-0000-0000-0002-000000000002"), 300, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000013"), new Guid("00000000-0000-0000-0002-000000000002"), 500, 0m },

                    // IN — Incandescente (no ballast loss modeled)
                    { new Guid("00000000-0000-0000-0003-000000000020"), new Guid("00000000-0000-0000-0002-000000000003"), 100, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000021"), new Guid("00000000-0000-0000-0002-000000000003"), 150, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000022"), new Guid("00000000-0000-0000-0002-000000000003"), 200, 0m },

                    // LD — LED (no ballast loss)
                    { new Guid("00000000-0000-0000-0003-000000000030"), new Guid("00000000-0000-0000-0002-000000000004"), 30,  0m },
                    { new Guid("00000000-0000-0000-0003-000000000031"), new Guid("00000000-0000-0000-0002-000000000004"), 50,  0m },
                    { new Guid("00000000-0000-0000-0003-000000000032"), new Guid("00000000-0000-0000-0002-000000000004"), 80,  0m },
                    { new Guid("00000000-0000-0000-0003-000000000033"), new Guid("00000000-0000-0000-0002-000000000004"), 100, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000034"), new Guid("00000000-0000-0000-0002-000000000004"), 150, 0m },
                    { new Guid("00000000-0000-0000-0003-000000000035"), new Guid("00000000-0000-0000-0002-000000000004"), 200, 0m },

                    // MT — Mista (ballast loss applies)
                    { new Guid("00000000-0000-0000-0003-000000000040"), new Guid("00000000-0000-0000-0002-000000000005"), 160, 30m },
                    { new Guid("00000000-0000-0000-0003-000000000041"), new Guid("00000000-0000-0000-0002-000000000005"), 250, 40m },
                    { new Guid("00000000-0000-0000-0003-000000000042"), new Guid("00000000-0000-0000-0002-000000000005"), 500, 60m },

                    // MV — Multivapor (ballast loss applies)
                    { new Guid("00000000-0000-0000-0003-000000000050"), new Guid("00000000-0000-0000-0002-000000000006"), 70,  10m },
                    { new Guid("00000000-0000-0000-0003-000000000051"), new Guid("00000000-0000-0000-0002-000000000006"), 150, 15m },
                    { new Guid("00000000-0000-0000-0003-000000000052"), new Guid("00000000-0000-0000-0002-000000000006"), 250, 25m },
                    { new Guid("00000000-0000-0000-0003-000000000053"), new Guid("00000000-0000-0000-0002-000000000006"), 400, 30m },

                    // VM — Vapor de Mercúrio (ballast loss applies)
                    { new Guid("00000000-0000-0000-0003-000000000060"), new Guid("00000000-0000-0000-0002-000000000007"), 80,  9m },
                    { new Guid("00000000-0000-0000-0003-000000000061"), new Guid("00000000-0000-0000-0002-000000000007"), 125, 11m },
                    { new Guid("00000000-0000-0000-0003-000000000062"), new Guid("00000000-0000-0000-0002-000000000007"), 250, 15m },
                    { new Guid("00000000-0000-0000-0003-000000000063"), new Guid("00000000-0000-0000-0002-000000000007"), 400, 22m },

                    // VS — Vapor de Sódio (ballast loss applies)
                    { new Guid("00000000-0000-0000-0003-000000000070"), new Guid("00000000-0000-0000-0002-000000000008"), 70,  8m },
                    { new Guid("00000000-0000-0000-0003-000000000071"), new Guid("00000000-0000-0000-0002-000000000008"), 100, 10m },
                    { new Guid("00000000-0000-0000-0003-000000000072"), new Guid("00000000-0000-0000-0002-000000000008"), 150, 14m },
                    { new Guid("00000000-0000-0000-0003-000000000073"), new Guid("00000000-0000-0000-0002-000000000008"), 250, 20m },
                    { new Guid("00000000-0000-0000-0003-000000000074"), new Guid("00000000-0000-0000-0002-000000000008"), 400, 30m },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Faturamentos");

            migrationBuilder.DropTable(
                name: "IpTransformerIndexes");

            migrationBuilder.DropTable(
                name: "LampWattageOptions");

            migrationBuilder.DropTable(
                name: "UnidadesOrganizacionais");

            migrationBuilder.DropTable(
                name: "LampTypes");

            migrationBuilder.DropTable(
                name: "Municipios");
        }
    }
}
