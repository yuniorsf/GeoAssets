using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderTargetsRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of "ServiceOrderTargets" tenant isolation (XD01-306) for the
            // isolated FieldService database. Same rule as the GeoAssets.Server history's
            // migration of the same name — a target follows its parent order — but spelled out
            // instead of inherited: in this database "ServiceOrders" carries no row-level-security
            // policy (InitialFieldServiceIsolation created the tables and the OrganizationGrantMirror
            // only), so an "EXISTS (parent order)" sub-select would be true for every row. The
            // grant clause is therefore written here, against "OrganizationGrantMirror" (the local
            // copy OrganizationGrantSyncWorker maintains), with the same conditions the shared
            // database's "ServiceOrders" policy applies to "OrganizationGrants".
            //
            // USING — own organization, or an active, unexpired, ServiceOrder-typed (or untyped)
            // grant from the parent order's organization whose ScopeId, when set, matches the
            // order's "scopeId" attribute.
            // WITH CHECK — additionally the row must carry its parent order's organization.
            //
            // The role needs SELECT on "ServiceOrders" and "OrganizationGrantMirror" in addition
            // to its privileges on this table.
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
                            JOIN "OrganizationGrantMirror" g ON g."ResourceOrganizationId" = o."OrganizationId"
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                              AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              AND g."IsActive"
                              AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                              AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                              AND (g."ScopeId" IS NULL OR g."ScopeId" = (o."AttributesJson"::jsonb ->> 'scopeId'))
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                              AND o."OrganizationId" = "ServiceOrderTargets"."OrganizationId"
                              AND (
                                  o."OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                                  OR EXISTS (
                                      SELECT 1 FROM "OrganizationGrantMirror" g
                                      WHERE g."ResourceOrganizationId" = o."OrganizationId"
                                        AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                                        AND g."IsActive"
                                        AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                                        AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                                        AND (g."ScopeId" IS NULL OR g."ScopeId" = (o."AttributesJson"::jsonb ->> 'scopeId'))
                                  )
                              )
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
