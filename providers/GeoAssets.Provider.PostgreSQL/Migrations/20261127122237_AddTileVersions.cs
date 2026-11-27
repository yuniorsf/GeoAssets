using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTileVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tile versions every kernel replica over this database shares (XD01-458, PostgresTileVersionStore): one
            // generation per scope (an organization id; all zeros = the shared features) and the one-row epoch/flush state.
            // No tenant data — counters only — so no Row-Level Security; the non-owner geoassets_app role a tenant-isolated
            // host connects as reads and advances them (created defensively, as the earlier migrations do).
            migrationBuilder.CreateTable(
                name: "tile_generations",
                columns: table => new
                {
                    Scope = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tile_generations", x => x.Scope);
                });

            migrationBuilder.CreateTable(
                name: "tile_version_state",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    Epoch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Flushes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tile_version_state", x => x.Id);
                    table.CheckConstraint("CK_tile_version_state_singleton", "\"Id\" = 1");
                });

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'geoassets_app') THEN
                        CREATE ROLE geoassets_app LOGIN;
                    END IF;
                END
                $$;

                GRANT SELECT, INSERT, UPDATE ON tile_generations TO geoassets_app;
                GRANT SELECT, INSERT, UPDATE ON tile_version_state TO geoassets_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tile_generations");

            migrationBuilder.DropTable(
                name: "tile_version_state");
        }
    }
}
