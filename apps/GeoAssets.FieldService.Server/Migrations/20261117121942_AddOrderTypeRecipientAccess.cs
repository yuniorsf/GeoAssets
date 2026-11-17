using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeRecipientAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecipientAccess",
                table: "OrderTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // XD01-333: what the organization an order was dispatched to may change depends on the
            // order's type — OrderType.RecipientAccess: 0 Inform (read the order and its targets),
            // 1 Execute (also record the work on each target), 2 Edit (also change the order). The
            // column above holds it; every type stored before this migration reads 0, which is what
            // XD01-316 left a recipient with on targets, and less than it left it on the order.
            //
            // The database-layer half of ServiceOrderRules' recipient ceiling, in the pattern of
            // AddServiceOrderDispatchRecipientRowLevelSecurity (XD01-316):
            //
            //   service_order_dispatched_to_current_organization_with_access(id, level)
            //       — STABLE SECURITY DEFINER, pinned search_path. True when the order is dispatched
            //         to the session's organization (service_order_dispatched_to_current_organization)
            //         and its type's "RecipientAccess" is at least "level". The type is matched
            //         ignoring letter case, as OrderTypeRegistry looks types up; a type with no row
            //         (registered only in code by a host), or a value outside 0..2, counts as 0 —
            //         never more access than the application gives, which reads both the same way.
            //         Should two rows differ only by case, the lowest level wins.
            //   service_order_target_stays_on_order(target id, order id)
            //       — STABLE SECURITY DEFINER, pinned search_path. The stored target with that id is
            //         on that order; called from a WITH CHECK it reads the row as it was before the
            //         statement, so it is true only for a new row version that left
            //         "ServiceOrderId" as it was.
            //
            // "ServiceOrders"
            //   dispatch_recipient_update_policy is restated on level 2: a recipient updates the
            //   order (an action, an annotation, the whole-order update all write its row) only when
            //   its type lets it edit the order. It still reads every order dispatched to it
            //   (dispatch_recipient_select_policy, unchanged), and organization_is_fixed_policy still
            //   stops it relabelling one.
            //
            // "ServiceOrderTargets"
            //   dispatch_recipient_update_policy — new, PERMISSIVE, FOR UPDATE, on level 1: the
            //   recipient of an order whose type lets it execute the order updates that order's
            //   targets (their activities, status and note), and only while the row stays on that
            //   order and keeps that order's organization. Which other columns it leaves alone is
            //   the application's to enforce (the target PATCH route changes nothing else). It still inserts and deletes none:
            //   adding or removing targets stays the owner's and grantees' (tenant_isolation_policy,
            //   unchanged).
            //
            //   In this history tenant_isolation_policy on "ServiceOrderTargets" spells
            //   owner-or-grantee out itself; it stays as it is. "OrderActionLogs" keeps following
            //   its order, as XD01-316 left it.
            //
            // No FORCE and no GRANT, as everywhere else in this history. EXECUTE on the functions is
            // PUBLIC's by default.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION service_order_dispatched_to_current_organization_with_access(p_order_id character varying, p_level integer)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT service_order_dispatched_to_current_organization(p_order_id)
                       AND COALESCE((
                               SELECT min(CASE WHEN t."RecipientAccess" BETWEEN 0 AND 2 THEN t."RecipientAccess" ELSE 0 END)
                               FROM "ServiceOrders" o
                               JOIN "OrderTypes" t ON lower(t."Id") = lower(o."OrderTypeId")
                               WHERE o."Id" = p_order_id
                           ), 0) >= p_level;
                $$;

                CREATE FUNCTION service_order_target_stays_on_order(p_target_id character varying, p_order_id character varying)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT EXISTS (
                        SELECT 1 FROM "ServiceOrderTargets" t
                        WHERE t."Id" = p_target_id
                          AND t."ServiceOrderId" = p_order_id
                    );
                $$;

                DROP POLICY IF EXISTS dispatch_recipient_update_policy ON "ServiceOrders";

                CREATE POLICY dispatch_recipient_update_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR UPDATE
                    USING (service_order_dispatched_to_current_organization_with_access("Id", 2))
                    WITH CHECK (service_order_dispatched_to_current_organization_with_access("Id", 2));

                CREATE POLICY dispatch_recipient_update_policy ON "ServiceOrderTargets"
                    AS PERMISSIVE
                    FOR UPDATE
                    USING (service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 1))
                    WITH CHECK (
                        service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 1)
                        AND service_order_target_stays_on_order("Id", "ServiceOrderId")
                        AND service_order_keeps_organization("ServiceOrderId", "OrganizationId")
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Puts both policies back as AddServiceOrderDispatchRecipientRowLevelSecurity left them,
            // removes the two functions, then the column; no order or target row is touched.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS dispatch_recipient_update_policy ON "ServiceOrderTargets";
                DROP POLICY IF EXISTS dispatch_recipient_update_policy ON "ServiceOrders";

                CREATE POLICY dispatch_recipient_update_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR UPDATE
                    USING (service_order_dispatched_to_current_organization("Id"))
                    WITH CHECK (service_order_dispatched_to_current_organization("Id"));

                DROP FUNCTION IF EXISTS service_order_target_stays_on_order(character varying, character varying);
                DROP FUNCTION IF EXISTS service_order_dispatched_to_current_organization_with_access(character varying, integer);
                """);

            migrationBuilder.DropColumn(
                name: "RecipientAccess",
                table: "OrderTypes");
        }
    }
}
