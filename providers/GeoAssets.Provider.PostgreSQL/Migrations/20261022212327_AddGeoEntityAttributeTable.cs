using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoEntityAttributeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "geo_entity_attribute",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geo_entity_attribute", x => new { x.EntityId, x.Key });
                    table.ForeignKey(
                        name: "FK_geo_entity_attribute_geo_entity_EntityId",
                        column: x => x.EntityId,
                        principalTable: "geo_entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // One-time backfill of existing custom_attributes jsonb into the new table — not
            // idempotent, runs once as part of this migration.
            migrationBuilder.Sql("""
                INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value")
                SELECT ge."Id", attr.key, attr.value
                FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_attribute_Key_Trgm",
                table: "geo_entity_attribute",
                column: "Key")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_geo_entity_attribute_Value_Trgm",
                table: "geo_entity_attribute",
                column: "Value")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geo_entity_attribute");
        }
    }
}
