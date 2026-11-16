using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIsolatedHistoryRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of tenant isolation for the five tables whose policies the
            // GeoAssets.Server history adds with hand-written SQL, which InitialFieldServiceIsolation
            // (generated from the model, XD01-290) therefore never carried (XD01-323). Until now only
            // the tables created after the split had a policy here.
            //
            // "ServiceOrders" — the GeoAssets.Server policy as UpdateServiceOrdersRowLevelSecurityForScopeId
            // leaves it (XD01-201 + XD01-250): own organization, or an active, unexpired,
            // ServiceOrder-typed (or untyped) grant from the row's organization whose ScopeId, when set,
            // equals the order's "scopeId" attribute. The one difference is the table the grant is read
            // from: "OrganizationGrants" lives in the other database, so the clause reads
            // "OrganizationGrantMirror", the local copy OrganizationGrantSyncWorker maintains with the
            // same columns. A grant change therefore takes effect here one sync interval later
            // (OrganizationGrantSyncOptions.PollInterval).
            //
            // "ContractorUsLedgerEntries" (XD01-202), "UsCatalogItems" (XD01-298), field_manhole_audits
            // (XD01-265), field_icerp_paving_measurements (XD01-297) — equality only, no grant exception,
            // character for character the GeoAssets.Server policies.
            //
            // "ServiceOrderTargets"' existing policy in this history spells the same grant clause out
            // against the mirror; it stays as it is and now agrees with its parent table's policy.
            //
            // A role subject to these policies needs SELECT on "OrganizationGrantMirror" in addition to
            // its privileges on the tables themselves. No GRANT is issued here: this database has no
            // application role yet, and the GeoAssets.Server history issues none for these tables either.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", as the GeoAssets.Server history does for
            // every one of these tables: every connection path still runs as the table owner today, and
            // FORCE would hide every row from it.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ServiceOrders" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "OrganizationGrantMirror" g
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
                            SELECT 1 FROM "OrganizationGrantMirror" g
                            WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                              AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              AND g."IsActive"
                              AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                              AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                              AND (g."ScopeId" IS NULL OR g."ScopeId" = ("ServiceOrders"."AttributesJson"::jsonb ->> 'scopeId'))
                        )
                    );

                ALTER TABLE "ContractorUsLedgerEntries" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ContractorUsLedgerEntries"
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);

                ALTER TABLE "UsCatalogItems" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "UsCatalogItems"
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);

                ALTER TABLE field_manhole_audits ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON field_manhole_audits
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);

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
            // Removes exactly what Up added; the tables and their rows are untouched.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON field_icerp_paving_measurements;
                ALTER TABLE field_icerp_paving_measurements DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON field_manhole_audits;
                ALTER TABLE field_manhole_audits DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON "UsCatalogItems";
                ALTER TABLE "UsCatalogItems" DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON "ContractorUsLedgerEntries";
                ALTER TABLE "ContractorUsLedgerEntries" DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrders";
                ALTER TABLE "ServiceOrders" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
