using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoEntityGeom3857 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // XD01-432 (ADR-002 D6): the vector tiles read geo_entity in Web Mercator.
            //
            // 1. geom becomes geometry(Geometry,4326): any geometry type, SRID 4326 only. A row stored without an SRID
            //    (0) takes 4326 — every writer stores longitude/latitude (RFC 7946) — and a row of another SRID is
            //    reprojected, so the constraint applies to every existing row. From now on PostGIS refuses a write of
            //    another SRID and gives an SRID-less one the column's. The rewrite rebuilds IX_geo_entity_geom.
            //
            // 2. geom_3857, a stored generated copy of geom in EPSG:3857, so a tile query neither reprojects per row
            //    nor misses an index: PostgreSQL computes it on every insert/update (writers never name it — EF Core
            //    does not map it) and backfills it for the existing rows right here. Web Mercator ends at
            //    ±85.0511287798066° of latitude; a geometry reaching beyond is clipped there first (a pole projects to
            //    infinity), leaving an empty geometry for one wholly beyond, which no tile draws.
            //
            // 3. Its GiST index, the access path of the tile query's ST_TileEnvelope filter.
            migrationBuilder.Sql(
                """
                ALTER TABLE geo_entity
                    ALTER COLUMN geom TYPE geometry(Geometry,4326)
                    USING CASE WHEN ST_SRID(geom) IN (0, 4326) THEN ST_SetSRID(geom, 4326) ELSE ST_Transform(geom, 4326) END;

                ALTER TABLE geo_entity
                    ADD COLUMN geom_3857 geometry(Geometry,3857) GENERATED ALWAYS AS (
                        CASE
                            WHEN ST_YMin(geom) >= -85.0511287798066 AND ST_YMax(geom) <= 85.0511287798066
                                THEN ST_Transform(geom, 3857)
                            ELSE ST_Transform(ST_ClipByBox2D(geom, 'BOX(-180 -85.0511287798066,180 85.0511287798066)'::box2d), 3857)
                        END) STORED;

                CREATE INDEX "IX_geo_entity_geom_3857" ON geo_entity USING GIST (geom_3857);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_geo_entity_geom_3857";
                ALTER TABLE geo_entity DROP COLUMN IF EXISTS geom_3857;
                ALTER TABLE geo_entity ALTER COLUMN geom TYPE geometry;
                """);
        }
    }
}
