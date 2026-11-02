using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <inheritdoc />
    public partial class AddPavingMeasurementsRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of field_icerp_paving_measurements tenant isolation (XD01-297),
            // mirroring AddManholeAuditsRowLevelSecurity's/AddContractorUsLedgerEntriesRowLevelSecurity's
            // shape — equality only, no grant-based exception: confirmed via InspectionCommitService
            // and WaterPavingSubOrderSpawner that the spawned child order's OrganizationId is always
            // copied from the parent order's, so a paving measurement row never spans two
            // organizations and has no cross-contractor delegation mechanism this ticket asks for.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE field_icerp_paving_measurements ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON field_icerp_paving_measurements
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
                DROP POLICY IF EXISTS tenant_isolation_policy ON field_icerp_paving_measurements;
                ALTER TABLE field_icerp_paving_measurements DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
