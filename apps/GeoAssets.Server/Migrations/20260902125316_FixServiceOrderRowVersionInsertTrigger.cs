using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class FixServiceOrderRowVersionInsertTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // InitialCreate's touch_service_orders_row_version trigger only fired BEFORE
            // UPDATE, so a brand-new row's RowVersion column (nullable: false, no default —
            // EF treats a rowVersion:true column as database-generated, the same way it
            // would SQL Server's native rowversion type) never got a value on INSERT,
            // violating the NOT NULL constraint on every Service Order creation. Extending
            // the trigger to also fire BEFORE INSERT gives new rows an initial RowVersion
            // the same way it already refreshes one on every UPDATE.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS service_orders_row_version_touch ON "ServiceOrders";

                CREATE TRIGGER service_orders_row_version_touch
                BEFORE INSERT OR UPDATE ON "ServiceOrders"
                FOR EACH ROW EXECUTE FUNCTION touch_service_orders_row_version();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS service_orders_row_version_touch ON "ServiceOrders";

                CREATE TRIGGER service_orders_row_version_touch
                BEFORE UPDATE ON "ServiceOrders"
                FOR EACH ROW EXECUTE FUNCTION touch_service_orders_row_version();
                """);
        }
    }
}
