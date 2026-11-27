using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoEntityTopologyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_topology",
                table: "geo_entity",
                column: "topology")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_geo_entity_topology",
                table: "geo_entity");
        }
    }
}
