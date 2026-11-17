using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <summary>
    /// Seeds the thematic styling of poles by occupancy (XD01-318): one point <c>layer</c> per
    /// occupancy class, and one <c>layer_rule</c> per class on the seeded pole asset type whose
    /// single condition matches the <c>occupancyClass</c> attribute an approval writes onto a pole.
    /// Global rows like the rest of the domain catalog (<c>layer</c> and <c>layer_rule</c> carry
    /// no organization), so no row-level-security change. See <c>PoleOccupancyThematicSeed</c>.
    /// </summary>
    public partial class SeedPoleOccupancyThematicLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "layer",
                columns: new[] { "Id", "Color", "DashArray", "FillColor", "FillOpacity", "GeometryType", "IconUrl", "Name", "Radius", "Weight" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0001-000000000006"), "#8839ef", null, "#8839ef", 0.20000000000000001, 0, "", "Poste — ocupante no identificado", 6.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-000000000007"), "#d20f39", null, "#d20f39", 0.20000000000000001, 0, "", "Poste — sobre capacidad", 6.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-000000000008"), "#df8e1d", null, "#df8e1d", 0.20000000000000001, 0, "", "Poste — ocupación completa", 6.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-000000000009"), "#40a02b", null, "#40a02b", 0.20000000000000001, 0, "", "Poste — ocupación baja", 6.0, 3.0 },
                    { new Guid("00000000-0000-0000-0001-00000000000a"), "#9ca0b0", null, "#9ca0b0", 0.20000000000000001, 0, "", "Poste — sin ocupantes", 6.0, 3.0 }
                });

            migrationBuilder.InsertData(
                table: "layer_rule",
                columns: new[] { "Id", "AssetTypeId", "LayerId", "Priority" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000001"), new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-000000000006"), 0 },
                    { new Guid("00000000-0000-0000-0002-000000000002"), new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-000000000007"), 1 },
                    { new Guid("00000000-0000-0000-0002-000000000003"), new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-000000000008"), 2 },
                    { new Guid("00000000-0000-0000-0002-000000000004"), new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-000000000009"), 3 },
                    { new Guid("00000000-0000-0000-0002-000000000005"), new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-00000000000a"), 4 }
                });

            migrationBuilder.InsertData(
                table: "layer_rule_condition",
                columns: new[] { "Id", "Attribute", "LayerRuleId", "Operator", "Value" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0003-000000000001"), "occupancyClass", new Guid("00000000-0000-0000-0002-000000000001"), 0, "Unidentified" },
                    { new Guid("00000000-0000-0000-0003-000000000002"), "occupancyClass", new Guid("00000000-0000-0000-0002-000000000002"), 0, "OverCapacity" },
                    { new Guid("00000000-0000-0000-0003-000000000003"), "occupancyClass", new Guid("00000000-0000-0000-0002-000000000003"), 0, "High" },
                    { new Guid("00000000-0000-0000-0003-000000000004"), "occupancyClass", new Guid("00000000-0000-0000-0002-000000000004"), 0, "Low" },
                    { new Guid("00000000-0000-0000-0003-000000000005"), "occupancyClass", new Guid("00000000-0000-0000-0002-000000000005"), 0, "None" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "layer_rule_condition",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000001"));

            migrationBuilder.DeleteData(
                table: "layer_rule_condition",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000002"));

            migrationBuilder.DeleteData(
                table: "layer_rule_condition",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000003"));

            migrationBuilder.DeleteData(
                table: "layer_rule_condition",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000004"));

            migrationBuilder.DeleteData(
                table: "layer_rule_condition",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000005"));

            migrationBuilder.DeleteData(
                table: "layer_rule",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000001"));

            migrationBuilder.DeleteData(
                table: "layer_rule",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000002"));

            migrationBuilder.DeleteData(
                table: "layer_rule",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000003"));

            migrationBuilder.DeleteData(
                table: "layer_rule",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000004"));

            migrationBuilder.DeleteData(
                table: "layer_rule",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000005"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000006"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000007"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000008"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000009"));

            migrationBuilder.DeleteData(
                table: "layer",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-00000000000a"));
        }
    }
}
