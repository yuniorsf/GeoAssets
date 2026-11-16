using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderChildTablesRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Row-level security for the three tenant-scoped tables that have no policy in either
            // history (XD01-323). They hold one organization's data but carry no column named
            // "OrganizationId", which is the only thing the "every organization-scoped table" test of
            // the shared-database suite looks for, so the GeoAssets.Server history never protected them.
            // Kept apart from AddIsolatedHistoryRowLevelSecurity because nothing here mirrors a
            // GeoAssets.Server migration.
            //
            // "OrderActionLogs", "OrderDispatches" — rows of an order, reachable only through
            // "ServiceOrderId" (ON DELETE CASCADE) and only ever loaded with their order. They follow
            // it, in the form the GeoAssets.Server history uses for "ServiceOrderTargets": the
            // sub-select names no organization and no grant, because a policy expression runs with the
            // querying role's privileges, so the "ServiceOrders" rows it finds are exactly the ones
            // that table's own policy (previous migration) shows that role. Whoever can see an order
            // can read and append its action log and its dispatches; nobody else can, and nobody can
            // attach a row to an order they cannot see.
            //
            // field_contractor_us_postings — equality on "ContractorOrganizationId", the same rule as
            // this table's EF query filter in ServiceOrderDbContext (applied to every read, never
            // ignored) and the same shape as "ContractorUsLedgerEntries"' policy.
            //
            // A role subject to these policies needs SELECT on "ServiceOrders" and, through that
            // table's policy, on "OrganizationGrantMirror". No GRANT and no FORCE, for the reasons given
            // in AddIsolatedHistoryRowLevelSecurity.
            migrationBuilder.Sql(
                """
                ALTER TABLE "OrderActionLogs" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "OrderActionLogs"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "OrderActionLogs"."ServiceOrderId"
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "OrderActionLogs"."ServiceOrderId"
                        )
                    );

                ALTER TABLE "OrderDispatches" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "OrderDispatches"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "OrderDispatches"."ServiceOrderId"
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "OrderDispatches"."ServiceOrderId"
                        )
                    );

                ALTER TABLE field_contractor_us_postings ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON field_contractor_us_postings
                    AS PERMISSIVE
                    FOR ALL
                    USING ("ContractorOrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("ContractorOrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes exactly what Up added; the tables and their rows are untouched.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON field_contractor_us_postings;
                ALTER TABLE field_contractor_us_postings DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON "OrderDispatches";
                ALTER TABLE "OrderDispatches" DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON "OrderActionLogs";
                ALTER TABLE "OrderActionLogs" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
