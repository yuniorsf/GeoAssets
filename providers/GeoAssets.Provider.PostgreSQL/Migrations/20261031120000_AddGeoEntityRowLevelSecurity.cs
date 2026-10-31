using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoEntityRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of tenant row isolation, underneath GeoAssetsDbContext's EF
            // Core global query filter (see its OnModelCreating). "OrganizationId" is quoted
            // because this table was never migrated to EFCore.NamingConventions' snake_case
            // style — the physical column is PascalCase, so an unquoted/lowercase reference in
            // the policy would silently never match (Postgres folds unquoted identifiers to
            // lowercase) and the policy would hide every row.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY": this table's owner is also the
            // role every host in this repo currently connects as, and FORCE applies RLS to the
            // owner too — turning the policy on for the owner today would hide every row for
            // every existing caller, since TenantSessionInterceptor isn't yet wired into every
            // connection path (PostgresProviderFactory/PostgresAssetProvider still construct
            // GeoAssetsDbContext directly, deliberately left unchanged — see its doc comment).
            // The policy is still created now so it already exists once a follow-up introduces
            // a dedicated non-owner application role; until then it's a documented no-op.
            migrationBuilder.Sql(
                """
                ALTER TABLE geo_entity ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON geo_entity
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
                DROP POLICY IF EXISTS tenant_isolation_policy ON geo_entity;
                ALTER TABLE geo_entity DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
