using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class UpdateServiceOrdersRowLevelSecurityForScopeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adds the OrganizationGrants.ScopeId condition (XD01-250) the original
            // AddServiceOrdersRowLevelSecurity policy never checked, even though the column
            // itself has existed since AddScopeIdToOrganizationGrant — a scoped grant
            // (ScopeId IS NOT NULL) now only authorizes rows whose own ServiceOrders.AttributesJson
            // carries a matching "scopeId" value (IScopeableResource.ScopeId's persisted form for
            // ServiceOrder; see that interface's own doc comment), mirroring CrossOrgGrantRule's
            // identical new check in C#. An ungated grant (ScopeId IS NULL) is unaffected.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrders";

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
                              AND (g."ScopeId" IS NULL OR g."ScopeId" = ("ServiceOrders"."AttributesJson"::jsonb ->> 'scopeId'))
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
                              AND (g."ScopeId" IS NULL OR g."ScopeId" = ("ServiceOrders"."AttributesJson"::jsonb ->> 'scopeId'))
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
    }
}
