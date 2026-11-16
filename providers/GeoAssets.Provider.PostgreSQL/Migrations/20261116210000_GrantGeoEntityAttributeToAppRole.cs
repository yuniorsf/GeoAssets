using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class GrantGeoEntityAttributeToAppRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Closes the gap XD01-307 reported: ForceGeoEntityRowLevelSecurity granted the
            // non-owner geoassets_app role privileges on geo_entity only, so a host connecting
            // as that role was refused (42501) by every statement touching geo_entity_attribute
            // — the identifier-list selection lookup, GetPageAsync's attribute search, and the
            // attribute rows written with each feature.
            //
            // geo_entity_attribute has no "OrganizationId" of its own: a row belongs to whoever
            // owns its geo_entity. Granting it without a policy would let that role read every
            // tenant's attribute values, so the same migration enables Row-Level Security on it
            // with a policy that defers to the parent row: an attribute is visible, and
            // writable, exactly when its geo_entity row is — and for a non-owner role that
            // sub-select is itself subject to geo_entity's (forced) tenant policy. Same shape
            // as "ServiceOrderTargets" inheriting from "ServiceOrders" (XD01-306).
            //
            // Not FORCEd, like every table but geo_entity: owner connections keep working as
            // before. The role is (re)created defensively, exactly as the earlier migration
            // does, and still without a password.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'geoassets_app') THEN
                        CREATE ROLE geoassets_app LOGIN;
                    END IF;
                END
                $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity_attribute TO geoassets_app;

                ALTER TABLE geo_entity_attribute ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON geo_entity_attribute
                    AS PERMISSIVE
                    FOR ALL
                    USING (EXISTS (
                        SELECT 1 FROM geo_entity e WHERE e."Id" = geo_entity_attribute."EntityId"))
                    WITH CHECK (EXISTS (
                        SELECT 1 FROM geo_entity e WHERE e."Id" = geo_entity_attribute."EntityId"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Leaves the geoassets_app role in place, as ForceGeoEntityRowLevelSecurity does.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation_policy ON geo_entity_attribute;
                ALTER TABLE geo_entity_attribute DISABLE ROW LEVEL SECURITY;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON geo_entity_attribute FROM geoassets_app;
                """);
        }
    }
}
