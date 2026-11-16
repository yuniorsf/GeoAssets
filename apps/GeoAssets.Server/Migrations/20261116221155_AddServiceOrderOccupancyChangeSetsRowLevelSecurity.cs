using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderOccupancyChangeSetsRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of tenant isolation for staged occupancy changes (XD01-315).
            //
            // Owner only, the same equality as field_pole_occupancies (AddPoleOccupanciesRowLevelSecurity),
            // and deliberately NOT "whoever can see the parent order", which is what the policy of
            // "ServiceOrderTargets" grants: a staged set is a proposed edit to the pole owner's
            // occupancy and names the occupants of its poles, so a cross-organization grantee on
            // the order — or, later, an occupant the order is dispatched to — gets no row here,
            // although it can see the order and its targets.
            //
            // WITH CHECK adds one condition to the equality: the row must sit under a target of
            // that same order and that same organization. Without it a tenant could insert a row
            // carrying its own organization under ANOTHER organization's target (a foreign key
            // check is not subject to row-level security): invisible to the owner, yet occupying
            // the one-staged-set-per-target unique index and so blocking the owner from staging.
            // The sub-select runs under the caller's own rights on "ServiceOrderTargets"; the
            // explicit organization equality is what pins it, whatever that table's policy shows.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ServiceOrderOccupancyChangeSets" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ServiceOrderOccupancyChangeSets"
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        AND EXISTS (
                            SELECT 1 FROM "ServiceOrderTargets" t
                            WHERE t."Id" = "ServiceOrderOccupancyChangeSets"."TargetId"
                              AND t."ServiceOrderId" = "ServiceOrderOccupancyChangeSets"."ServiceOrderId"
                              AND t."OrganizationId" = "ServiceOrderOccupancyChangeSets"."OrganizationId"
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrderOccupancyChangeSets";
                ALTER TABLE "ServiceOrderOccupancyChangeSets" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
