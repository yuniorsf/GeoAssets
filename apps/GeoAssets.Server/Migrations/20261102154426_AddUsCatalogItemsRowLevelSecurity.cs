using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <inheritdoc />
    public partial class AddUsCatalogItemsRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of UsCatalogItems tenant isolation (XD01-298), mirroring
            // AddContractorUsLedgerEntriesRowLevelSecurity's/AddManholeAuditsRowLevelSecurity's/
            // AddPavingMeasurementsRowLevelSecurity's shape — equality only, no grant-based
            // exception. IUsCatalogRepository's own doc comment already documents this as the
            // intended shape ("Scoped to the caller's own tenant ... no cross-contractor grant
            // exception"), and EFUsCatalogRepository's query methods (FindEffectiveAsync/
            // GetByOrganizationAsync) already filter by OrganizationId with no cross-org path —
            // the database-layer policy enforcing it was simply never written.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE "UsCatalogItems" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "UsCatalogItems"
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
                DROP POLICY IF EXISTS tenant_isolation_policy ON "UsCatalogItems";
                ALTER TABLE "UsCatalogItems" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
