using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderTargetsRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of "ServiceOrderTargets" tenant isolation (XD01-306). A target
            // has no visibility of its own: it follows its parent order.
            //
            // USING — a row is visible to its own organization, or to whoever can see the parent
            // order. The EXISTS sub-select names no organization or grant on purpose: a policy
            // expression runs with the querying role's privileges, so the "ServiceOrders" rows it
            // can find are exactly the ones "ServiceOrders"' own tenant_isolation_policy lets that
            // role see (own organization, or an active OrganizationGrant, ScopeId included — see
            // UpdateServiceOrdersRowLevelSecurityForScopeId). Whatever that policy becomes, this
            // one follows without being edited. CrossTenantRowLevelSecurityTests proves it for a
            // non-owner role, including that a tenant with no grant gets nothing through it.
            //
            // WITH CHECK — a row may only be written under a parent order the role can see, and
            // must carry that order's organization. That lets a grantee who works on a granted
            // order add, edit and release its targets (as it may already edit the order itself),
            // while nobody can attach a target to an order they cannot see, file one under a
            // different organization than its order's, or move one to another organization.
            //
            // The role therefore needs SELECT on "ServiceOrders" (and, through that table's
            // policy, on "OrganizationGrants") in addition to its privileges on this table.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ServiceOrderTargets" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ServiceOrderTargets"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                              AND o."OrganizationId" = "ServiceOrderTargets"."OrganizationId"
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrderTargets";
                ALTER TABLE "ServiceOrderTargets" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
