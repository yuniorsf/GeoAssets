using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderRowVersionTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "ServiceOrders"."RowVersion" in this database had nothing that ever gave it a value
            // (XD01-323). The column is bytea, NOT NULL, with no default, and the model declares it
            // IsRowVersion(), so EF Core treats it as database-generated: it never sends a value on
            // INSERT or UPDATE and compares the one it read in the UPDATE's WHERE clause. PostgreSQL has
            // no auto-updating row version type, so the GeoAssets.Server history fills the column with a
            // trigger (InitialCreate, widened to INSERT by FixServiceOrderRowVersionInsertTrigger). Both
            // are hand-written SQL, so InitialFieldServiceIsolation, generated from the model, carried
            // neither: here every order created through EF failed the NOT NULL constraint, and an update
            // would never have changed the token, so a stale writer was not detected.
            //
            // This is the function and the trigger exactly as the GeoAssets.Server history ends up with
            // them, same names included. Rows already stored keep their value until their next update.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION touch_service_orders_row_version() RETURNS trigger AS $$
                BEGIN
                    NEW."RowVersion" := uuid_send(gen_random_uuid());
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS service_orders_row_version_touch ON "ServiceOrders";

                CREATE TRIGGER service_orders_row_version_touch
                BEFORE INSERT OR UPDATE ON "ServiceOrders"
                FOR EACH ROW EXECUTE FUNCTION touch_service_orders_row_version();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to InitialFieldServiceIsolation's state: no trigger, no function. Stored values stay.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS service_orders_row_version_touch ON "ServiceOrders";
                DROP FUNCTION IF EXISTS touch_service_orders_row_version();
                """);
        }
    }
}
