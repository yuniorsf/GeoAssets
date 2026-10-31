using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrdersRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of tenant row isolation for ServiceOrders (XD01-201),
            // mirroring GeoAssets.Provider.PostgreSQL's AddGeoEntityRowLevelSecurity migration
            // — see that migration's own doc comment for why every identifier here is quoted
            // (this table was never migrated to EFCore.NamingConventions' snake_case style, so
            // the physical columns are PascalCase).
            //
            // Unlike geo_entity, ServiceOrders already has a real cross-org delegation
            // mechanism — CrossOrgGrantRule/WorkflowOrgGrant (core/GeoAssets.Workflow/Rules),
            // backed by the persisted OrganizationGrant entity (table "OrganizationGrants",
            // GeoIdentityDbContext). The policy's USING/WITH CHECK clauses mirror that rule's
            // own matching logic — grantee org, resource org, optional ResourceType scoped to
            // "ServiceOrder" (nameof(GeoAssets.Workflow.Orders.ServiceOrder)), active and
            // unexpired — so a raw SQL/ADO.NET query is held to exactly the same cross-org
            // boundary ServiceOrderRules already enforces in C#, not a stricter one.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as geo_entity's
            // migration: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ServiceOrders" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "OrganizationGrants" g
                            WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                              AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              AND g."IsActive"
                              AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                              AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                        )
                    )
                    WITH CHECK (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "OrganizationGrants" g
                            WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                              AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              AND g."IsActive"
                              AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                              AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrders";
                ALTER TABLE "ServiceOrders" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
