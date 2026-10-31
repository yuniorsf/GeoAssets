using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class ForceGeoEntityRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Closes the gap AddGeoEntityRowLevelSecurity deliberately left open: that migration
            // enabled RLS on geo_entity but could not FORCE it, because every connection path
            // still ran as the table owner (RLS never restricts owners unless FORCE is also set)
            // and no non-owner application role existed to connect as instead.
            // ITenantContext is now wired end-to-end to the authenticated request's organization
            // claim (HttpTenantContext, XD01-200), so this migration introduces that non-owner
            // role and forces the policy for real.
            //
            // The role is created with LOGIN but no password — a migration must never embed a
            // real credential. An operator sets one out-of-band (e.g. `ALTER ROLE geoassets_app
            // PASSWORD '...'` via a secrets-managed deploy step) before pointing
            // ConnectionStrings:GeoAssets at it. Until that happens, every existing deployment
            // keeps connecting as the table owner exactly as before — this migration alone does
            // not change which role the app connects as.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'geoassets_app') THEN
                        CREATE ROLE geoassets_app LOGIN;
                    END IF;
                END
                $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity TO geoassets_app;

                ALTER TABLE geo_entity FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Leaves the geoassets_app role itself in place — a later migration may have already
            // granted it privileges on other tables, so dropping the role here could break that.
            migrationBuilder.Sql(
                """
                ALTER TABLE geo_entity NO FORCE ROW LEVEL SECURITY;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON geo_entity FROM geoassets_app;
                """);
        }
    }
}
