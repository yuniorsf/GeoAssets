using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeRecipientActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderTypeRecipientActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    FromStateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ToStateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTypeRecipientActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderTypeRecipientActions_OrderTypeId_Action",
                table: "OrderTypeRecipientActions",
                columns: new[] { "OrderTypeId", "Action" });

            // XD01-336: an order type may let the recipients of its orders take some order actions on
            // top of its recipient access (OrderType.RecipientActions) — the shared-use occupant notice
            // lets its occupant accept it and comment on it, and nothing else. The table above holds
            // them, keyed by the type's id with no foreign key to "OrderTypes": the notice is
            // registered in code and has no row there, so its two actions are seeded here. Per action,
            // one row grants it with no move ("FromStateKey"/"ToStateKey" NULL), and one row per
            // transition of the type tagged with it gives the move it makes — 4 = Accept, moving a
            // notice from Pending to InProgress; 9 = Annotate, which moves nothing. The numbers are
            // OrderActionType's own; a type stored through EFOrderTypeRepository writes its own rows.
            //
            // The database-layer half of ServiceOrderRules.EvaluateAppend, in the pattern of
            // AddServiceOrderDispatchRecipientRowLevelSecurity (XD01-316) and
            // AddOrderTypeRecipientAccess (XD01-333). Three functions, all STABLE SECURITY DEFINER with
            // a pinned search_path; the type is matched ignoring letter case, as OrderTypeRegistry does:
            //
            //   service_order_recipient_actions_granted(id)
            //       — the order is dispatched to the session's organization and its type lets a
            //         recipient take at least one action.
            //   service_order_recipient_action_is_granted(id, action)
            //       — the order is dispatched to the session's organization and its type lets a
            //         recipient take that action.
            //   service_order_recipient_change_is_granted(new row of "ServiceOrders")
            //       — the order is dispatched to the session's organization, and the new row differs
            //         from the stored one (read, from a WITH CHECK, as it was before the statement) in
            //         nothing but "Status", "UpdatedAt" and "RowVersion" (stamped by every action and
            //         by the row-version trigger), and its status either stays as it is or moves
            //         along a move its type grants a recipient. Title, description, type, assignee,
            //         completion, parent, attributes, features, selection, organization: all fixed.
            //
            // "ServiceOrders"
            //   dispatch_recipient_action_update_policy — PERMISSIVE, FOR UPDATE: the recipient of an
            //   order whose type grants it an action may write the row that action writes, and only
            //   that (an accepted notice goes from Pending to InProgress; a comment only stamps
            //   "UpdatedAt"). dispatch_recipient_update_policy (Edit types) and
            //   organization_is_fixed_policy are unchanged. A recipient still inserts and deletes none.
            //
            // "OrderActionLogs"
            //   tenant_isolation_policy — owner or grantee (service_order_owned_by_or_granted_to_current_organization),
            //   every command, as on "OrderDispatches".
            //   dispatch_recipient_select_policy — a recipient reads the log of an order dispatched to it.
            //   dispatch_recipient_insert_policy — a recipient appends an entry only for an action its
            //   order's type grants it, or any entry on an order whose type lets it edit the order
            //   (RecipientAccess 2, where it takes every action its rules allow). It never updates or
            //   deletes an entry.
            //   This table had no row-level security in this history (XD01-323's finding): any session
            //   could append to any order's log. It is enabled here.
            //
            // No FORCE and no GRANT, as everywhere else in this history.
            migrationBuilder.Sql(
                """
                INSERT INTO "OrderTypeRecipientActions" ("OrderTypeId", "Action", "FromStateKey", "ToStateKey") VALUES
                    ('shared-use-occupant-notice', 4, NULL, NULL),
                    ('shared-use-occupant-notice', 4, 'Pending', 'InProgress'),
                    ('shared-use-occupant-notice', 9, NULL, NULL);

                CREATE FUNCTION service_order_recipient_actions_granted(p_order_id character varying)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT service_order_dispatched_to_current_organization(p_order_id)
                       AND EXISTS (
                               SELECT 1
                               FROM "ServiceOrders" o
                               JOIN "OrderTypeRecipientActions" a ON lower(a."OrderTypeId") = lower(o."OrderTypeId")
                               WHERE o."Id" = p_order_id
                           );
                $$;

                CREATE FUNCTION service_order_recipient_action_is_granted(p_order_id character varying, p_action integer)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT service_order_dispatched_to_current_organization(p_order_id)
                       AND EXISTS (
                               SELECT 1
                               FROM "ServiceOrders" o
                               JOIN "OrderTypeRecipientActions" a ON lower(a."OrderTypeId") = lower(o."OrderTypeId")
                               WHERE o."Id" = p_order_id
                                 AND a."Action" = p_action
                           );
                $$;

                CREATE FUNCTION service_order_recipient_change_is_granted(p_order "ServiceOrders")
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT service_order_dispatched_to_current_organization(p_order."Id")
                       AND EXISTS (
                               SELECT 1
                               FROM "ServiceOrders" o
                               JOIN "OrderTypeRecipientActions" a ON lower(a."OrderTypeId") = lower(o."OrderTypeId")
                               WHERE o."Id" = p_order."Id"
                                 AND (to_jsonb(o) - 'Status' - 'UpdatedAt' - 'RowVersion')
                                     = (to_jsonb(p_order) - 'Status' - 'UpdatedAt' - 'RowVersion')
                                 AND (
                                         p_order."Status" = o."Status"
                                         OR (a."FromStateKey" = o."Status" AND a."ToStateKey" = p_order."Status")
                                     )
                           );
                $$;

                CREATE POLICY dispatch_recipient_action_update_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR UPDATE
                    USING (service_order_recipient_actions_granted("Id"))
                    WITH CHECK (service_order_recipient_change_is_granted("ServiceOrders"));

                ALTER TABLE "OrderActionLogs" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "OrderActionLogs"
                    AS PERMISSIVE
                    FOR ALL
                    USING (service_order_owned_by_or_granted_to_current_organization("ServiceOrderId"))
                    WITH CHECK (service_order_owned_by_or_granted_to_current_organization("ServiceOrderId"));

                CREATE POLICY dispatch_recipient_select_policy ON "OrderActionLogs"
                    AS PERMISSIVE
                    FOR SELECT
                    USING (service_order_dispatched_to_current_organization("ServiceOrderId"));

                CREATE POLICY dispatch_recipient_insert_policy ON "OrderActionLogs"
                    AS PERMISSIVE
                    FOR INSERT
                    WITH CHECK (
                        service_order_recipient_action_is_granted("ServiceOrderId", "Action")
                        OR service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 2)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Puts "OrderActionLogs" back as the previous migration left it, removes the policy and
            // the functions, then the table; no order or log row is touched.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS dispatch_recipient_insert_policy ON "OrderActionLogs";
                DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "OrderActionLogs";
                DROP POLICY IF EXISTS tenant_isolation_policy ON "OrderActionLogs";
                ALTER TABLE "OrderActionLogs" DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS dispatch_recipient_action_update_policy ON "ServiceOrders";

                DROP FUNCTION IF EXISTS service_order_recipient_change_is_granted("ServiceOrders");
                DROP FUNCTION IF EXISTS service_order_recipient_action_is_granted(character varying, integer);
                DROP FUNCTION IF EXISTS service_order_recipient_actions_granted(character varying);
                """);

            migrationBuilder.DropTable(
                name: "OrderTypeRecipientActions");
        }
    }
}
