using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAppliedMutationBatchesRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Closes XD01-296 (applied_mutation_batches had an OrganizationId column but no RLS
            // policy at all — discovered by the same generic discovery test that caught
            // asset_type under XD01-288). Equality only, no grant-based exception, mirroring
            // AddContractorUsLedgerEntriesRowLevelSecurity's/AddManholeAuditsRowLevelSecurity's
            // shape: a redelivered-batch idempotency record (EFAppliedMutationBatchStore) has no
            // cross-org delegation mechanism anywhere in this codebase — IAppliedMutationBatchStore
            // is looked up strictly by BatchId within the caller's own organization, never shared
            // across tenants the way ServiceOrders' OrganizationGrants exception requires.
            //
            // "OrganizationId" is quoted for the same reason it is on geo_entity/ServiceOrders/
            // ContractorUsLedgerEntries/asset_type: no naming-convention plugin is registered on
            // GeoAssetsDbContext, so EF's default column naming (verbatim C# property name) applies
            // and Npgsql quotes the resulting mixed-case identifier.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", same reasoning as every other RLS
            // migration in this repo: every connection path still runs as the table owner today.
            migrationBuilder.Sql(
                """
                ALTER TABLE applied_mutation_batches ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON applied_mutation_batches
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
                DROP POLICY IF EXISTS tenant_isolation_policy ON applied_mutation_batches;
                ALTER TABLE applied_mutation_batches DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
