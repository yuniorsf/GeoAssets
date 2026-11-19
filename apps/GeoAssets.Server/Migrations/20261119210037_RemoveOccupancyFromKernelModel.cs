using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <summary>
    /// XD01-380: the kernel's ServiceOrderDbContext no longer maps pole occupancies, their entries or the occupancy change
    /// sets — the NeutralOperator vertical's NeutralOperatorDbContext does, with its own migration chain, whose baseline
    /// (AdoptOccupancyTables) moves these very tables, rows and row-level-security policies included, into schema
    /// neutral_operator. This migration exists for its model snapshot only and does nothing to the database: the
    /// DropTable (and, in Down, CreateTable/CreateIndex/foreign-key) operations EF scaffolded for field_pole_occupancies,
    /// field_pole_occupancy_entries and "ServiceOrderOccupancyChangeSets" were removed by hand — the tables hold data and
    /// are owned by the other chain now. Down is likewise a no-op: they still exist, in neutral_operator (or, once that
    /// chain's baseline is reverted, back in public), and must not be recreated here.
    /// </summary>
    public partial class RemoveOccupancyFromKernelModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see the class summary.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see the class summary.
        }
    }
}
