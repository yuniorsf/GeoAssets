using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddLayerAndLayerRuleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "layer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GeometryType = table.Column<int>(type: "integer", nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "#3388ff"),
                    Radius = table.Column<double>(type: "double precision", nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    DashArray = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FillColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "#3388ff"),
                    FillOpacity = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "layer_rule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layer_rule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_layer_rule_asset_type_AssetTypeId",
                        column: x => x.AssetTypeId,
                        principalTable: "asset_type",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_layer_rule_layer_LayerId",
                        column: x => x.LayerId,
                        principalTable: "layer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "layer_rule_condition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LayerRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attribute = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layer_rule_condition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_layer_rule_condition_layer_rule_LayerRuleId",
                        column: x => x.LayerRuleId,
                        principalTable: "layer_rule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_layer_rule_AssetTypeId",
                table: "layer_rule",
                column: "AssetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_layer_rule_LayerId",
                table: "layer_rule",
                column: "LayerId");

            migrationBuilder.CreateIndex(
                name: "IX_layer_rule_condition_LayerRuleId",
                table: "layer_rule_condition",
                column: "LayerRuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "layer_rule_condition");

            migrationBuilder.DropTable(
                name: "layer_rule");

            migrationBuilder.DropTable(
                name: "layer");
        }
    }
}
