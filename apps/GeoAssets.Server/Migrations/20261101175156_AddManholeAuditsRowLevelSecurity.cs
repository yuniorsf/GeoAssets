using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddManholeAuditsRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of field_manhole_audits tenant isolation (XD01-265), mirroring
            // AddContractorUsLedgerEntriesRowLevelSecurity's shape — equality only, no grant-based
            // exception: a manhole audit, like a ledger entry, has no cross-contractor
            // delegation mechanism this ticket asks for.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE field_manhole_audits ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON field_manhole_audits
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
                DROP POLICY IF EXISTS tenant_isolation_policy ON field_manhole_audits;
                ALTER TABLE field_manhole_audits DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
