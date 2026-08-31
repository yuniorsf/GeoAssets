using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class ConstrainGeoEntityLayerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Verified before authoring this migration (SELECT DISTINCT "LayerId" FROM geo_entity
            // WHERE "LayerId" <> '') that every existing row has LayerId = '' — nothing writes it
            // yet. '' isn't valid uuid syntax, so the type change needs an explicit USING clause
            // mapping '' -> NULL; EF's default AlterColumn (no USING clause) would fail on these rows.
            migrationBuilder.Sql(
                """
                ALTER TABLE geo_entity
                    ALTER COLUMN "LayerId" DROP DEFAULT,
                    ALTER COLUMN "LayerId" TYPE uuid USING NULLIF("LayerId", '')::uuid,
                    ALTER COLUMN "LayerId" DROP NOT NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_geo_entity_layer_LayerId",
                table: "geo_entity",
                column: "LayerId",
                principalTable: "layer",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_geo_entity_layer_LayerId",
                table: "geo_entity");

            migrationBuilder.Sql(
                """
                ALTER TABLE geo_entity
                    ALTER COLUMN "LayerId" TYPE character varying(36) USING COALESCE("LayerId"::text, ''),
                    ALTER COLUMN "LayerId" SET DEFAULT '',
                    ALTER COLUMN "LayerId" SET NOT NULL;
                """);
        }
    }
}
