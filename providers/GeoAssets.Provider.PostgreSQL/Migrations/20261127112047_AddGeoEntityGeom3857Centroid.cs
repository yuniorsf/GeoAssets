using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoEntityGeom3857Centroid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // XD01-457 (ADR-002 D6): the vector tiles' aggregate tier reads each feature's centroid in Web Mercator.
            //
            // 1. geom_3857_centroid, a stored generated ST_Centroid of geom_3857, so an aggregate tile neither computes a
            //    centroid per row nor detoasts a polygon or line to do so: PostgreSQL computes it on every insert/update
            //    (writers never name it — EF Core does not map it) and backfills it for the existing rows right here, a
            //    table rewrite. A generated column cannot read another one, so the expression repeats geom_3857's; the
            //    stored point is ST_Centroid(geom_3857) bit for bit. NULL for a row without a geometry, an empty point
            //    for one wholly beyond Web Mercator's latitudes (no tile draws it).
            //
            // 2. Its GiST index, the access path of the aggregate tier's ST_TileEnvelope filter.
            migrationBuilder.Sql(
                """
                ALTER TABLE geo_entity
                    ADD COLUMN geom_3857_centroid geometry(Point,3857) GENERATED ALWAYS AS (
                        ST_Centroid(CASE
                            WHEN ST_YMin(geom) >= -85.0511287798066 AND ST_YMax(geom) <= 85.0511287798066
                                THEN ST_Transform(geom, 3857)
                            ELSE ST_Transform(ST_ClipByBox2D(geom, 'BOX(-180 -85.0511287798066,180 85.0511287798066)'::box2d), 3857)
                        END)) STORED;

                CREATE INDEX "IX_geo_entity_geom_3857_centroid" ON geo_entity USING GIST (geom_3857_centroid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_geo_entity_geom_3857_centroid";
                ALTER TABLE geo_entity DROP COLUMN IF EXISTS geom_3857_centroid;
                """);
        }
    }
}
