using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <summary>
    /// XD01-381: the kernel's ServiceOrderDbContext no longer maps the contractor US ledger, the US catalog, the contractor
    /// US postings, the ICERP paving measurements or the manhole audits — the FieldService vertical's FieldServiceDbContext
    /// does, with its own migration chain, whose baseline (AdoptFieldServiceTables) moves these very tables, rows and
    /// row-level-security policies included, into schema field_service. This migration exists for its model snapshot only
    /// and does nothing to the database: the DropTable operations EF scaffolded for "ContractorUsLedgerEntries",
    /// "UsCatalogItems", field_contractor_us_postings, field_icerp_paving_measurements and field_manhole_audits (and, in
    /// Down, their CreateTable/CreateIndex/foreign-key operations) were removed by hand — the tables hold this host's data
    /// and are owned by the other chain now. Down is likewise a no-op: they still exist, in field_service (or, once that
    /// chain's baseline is reverted, back in public), and must not be recreated here.
    /// </summary>
    public partial class RemoveFieldServiceTablesFromKernelModel : Migration
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
