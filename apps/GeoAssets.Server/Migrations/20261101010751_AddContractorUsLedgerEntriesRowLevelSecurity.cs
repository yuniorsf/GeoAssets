using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddContractorUsLedgerEntriesRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of ContractorUsLedgerEntries tenant isolation (XD01-202),
            // mirroring GeoAssets.Provider.PostgreSQL's AddGeoEntityRowLevelSecurity migration —
            // equality only, no grant-based exception (unlike AddServiceOrdersRowLevelSecurity):
            // the ticket explicitly calls for this simpler shape since a ledger entry has no
            // cross-contractor delegation mechanism.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as geo_entity's and
            // ServiceOrders' own RLS migrations: every connection path still runs as the table
            // owner today, and FORCE would hide every row until a dedicated non-owner app role
            // exists.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ContractorUsLedgerEntries" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ContractorUsLedgerEntries"
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON "ContractorUsLedgerEntries";
                ALTER TABLE "ContractorUsLedgerEntries" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
