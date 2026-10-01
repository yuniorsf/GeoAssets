using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGetPageAsyncSearchSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_CreatedAt",
                table: "geo_entity",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_custom_attributes",
                table: "geo_entity",
                column: "custom_attributes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_Description_Trgm",
                table: "geo_entity",
                column: "Description")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_Name",
                table: "geo_entity",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_Name_Trgm",
                table: "geo_entity",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_UpdatedAt",
                table: "geo_entity",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_geo_entity_CreatedAt",
                table: "geo_entity");

            migrationBuilder.DropIndex(
                name: "IX_geo_entity_custom_attributes",
                table: "geo_entity");

            migrationBuilder.DropIndex(
                name: "IX_geo_entity_Description_Trgm",
                table: "geo_entity");

            migrationBuilder.DropIndex(
                name: "IX_geo_entity_Name",
                table: "geo_entity");

            migrationBuilder.DropIndex(
                name: "IX_geo_entity_Name_Trgm",
                table: "geo_entity");

            migrationBuilder.DropIndex(
                name: "IX_geo_entity_UpdatedAt",
                table: "geo_entity");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
