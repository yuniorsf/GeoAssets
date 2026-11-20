using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.NeutralOperator.Persistence.Migrations
{
    /// <summary>
    /// XD01-392: <c>occupancy_apply_sagas.UnrestoredFeatureIdsJson</c> — the poles a reverted saga's compensation left
    /// untouched because a later change had moved them on (JSON array; null when every pole was restored). Additive and
    /// nullable: no existing row changes.
    /// </summary>
    public partial class AddOccupancyApplySagaUnrestoredFeatureIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnrestoredFeatureIdsJson",
                schema: "neutral_operator",
                table: "occupancy_apply_sagas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnrestoredFeatureIdsJson",
                schema: "neutral_operator",
                table: "occupancy_apply_sagas");
        }
    }
}
