using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeTargetPolicyAndAttributesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributesSchemaJson",
                table: "OrderTypes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPolicyJson",
                table: "OrderTypes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttributesSchemaJson",
                table: "OrderTypes");

            migrationBuilder.DropColumn(
                name: "TargetPolicyJson",
                table: "OrderTypes");
        }
    }
}
