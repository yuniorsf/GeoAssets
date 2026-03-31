using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "asset_type",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "#3388ff"),
                    IconUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_type", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "geo_entity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssetTypeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: ""),
                    LayerId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    geom = table.Column<Geometry>(type: "geometry", nullable: true),
                    custom_attributes = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    topology = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AssetTypeId1 = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geo_entity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_geo_entity_asset_type_AssetTypeId1",
                        column: x => x.AssetTypeId1,
                        principalTable: "asset_type",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "asset_type",
                columns: new[] { "Id", "Color", "IconUrl", "IsBuiltIn", "Name" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "#e74c3c", "", true, "Punto de interés" },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "#3498db", "", true, "Línea" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "#2ecc71", "", true, "Área" }
                });

            // GiST spatial index on the PostGIS geometry column
            migrationBuilder.Sql(
                "CREATE INDEX IX_geo_entity_geom ON geo_entity USING GIST (geom);");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_AssetTypeId",
                table: "geo_entity",
                column: "AssetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_AssetTypeId1",
                table: "geo_entity",
                column: "AssetTypeId1");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_LayerId",
                table: "geo_entity",
                column: "LayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_geo_entity_geom;");

            migrationBuilder.DropTable(
                name: "geo_entity");

            migrationBuilder.DropTable(
                name: "asset_type");
        }
    }
}