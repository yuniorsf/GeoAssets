using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTypeRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Closes XD01-288 (asset_type had an OrganizationId column but no RLS policy at
            // all — a real cross-tenant leak, discovered by XD01-204's own discovery test).
            // Same shape and same caveat as AddGeoEntityRowLevelSecurity: "OrganizationId" is
            // quoted (this table predates EFCore.NamingConventions' snake_case style, same as
            // geo_entity — confirmed via AddOrganizationOwnership, which added this column with
            // PascalCase on both tables in the same migration).
            //
            // Unlike geo_entity, asset_type has real rows with OrganizationId = Guid.Empty —
            // AssetTypeRowConfiguration's HasData seeds the 3 generic defaults (Point/Line/Area)
            // and the domain starter catalog (Pole/Transformer/...) with no OrganizationId set,
            // relying on its HasDefaultValue(Guid.Empty). These are meant to be visible to every
            // tenant (global built-ins), not owned by one — so USING must allow
            // Guid.Empty-owned rows for every session, not just an exact match on the caller's
            // own organization. WITH CHECK intentionally does NOT grant that same exception: a
            // tenant writing a row must always claim its own OrganizationId, never Guid.Empty,
            // so an ordinary caller can never mint a new "global" asset type visible to every
            // other tenant.
            //
            // Deliberately omits "FORCE ROW LEVEL SECURITY", for the same reason
            // AddGeoEntityRowLevelSecurity did: every connection path still runs as the table
            // owner today (no deploy has opted into the geoassets_app role
            // ForceGeoEntityRowLevelSecurity introduced), and RLS never restricts an owner
            // without FORCE — including the owner-run HasData seed INSERTs above. The policy is
            // still created now so it already exists once a follow-up extends geoassets_app's
            // grants to this table and forces it, mirroring ForceGeoEntityRowLevelSecurity;
            // until then it's a documented no-op.
            migrationBuilder.Sql(
                """
                ALTER TABLE asset_type ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON asset_type
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR "OrganizationId" = '00000000-0000-0000-0000-000000000000'::uuid
                    )
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON asset_type;
                ALTER TABLE asset_type DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
