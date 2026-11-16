using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPoleOccupanciesRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of pole occupancy tenant isolation (XD01-314), for both of its
            // tables: field_pole_occupancies (one row per pole) and field_pole_occupancy_entries
            // (its attachment points). Same shape as AddManholeAuditsRowLevelSecurity — equality
            // only, no grant-based exception.
            //
            // Unlike this history's "ServiceOrderTargets" policy, nothing here reads
            // "OrganizationGrantMirror": a mirrored grant never opens occupancy to the grantee, so
            // the policy is the plain equality and the role needs privileges on these two tables only.
            //
            // Owner only on purpose: occupancy is the pole owner's data. An occupant organization
            // gets no row through these policies, not even the entries whose
            // "OccupantOrganizationId" names it; it learns about work on a pole through the order
            // dispatched to it, never by reading these tables. "OrganizationId" on an entry
            // repeats its pole's — the composite foreign key to (pole "Id", "OrganizationId")
            // guarantees it — so each table is isolated by its own column and neither policy
            // needs a sub-select into the other.
            //
            // One migration for the two tables, where this history otherwise has one RLS migration
            // per table: they are one aggregate, created by one migration (AddPoleOccupancies), and
            // must never be deployed with only one of them protected.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE field_pole_occupancies ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON field_pole_occupancies
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);

                ALTER TABLE field_pole_occupancy_entries ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON field_pole_occupancy_entries
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
                DROP POLICY IF EXISTS tenant_isolation_policy ON field_pole_occupancy_entries;
                ALTER TABLE field_pole_occupancy_entries DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON field_pole_occupancies;
                ALTER TABLE field_pole_occupancies DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
