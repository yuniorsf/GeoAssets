using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <inheritdoc />
    public partial class AddOrderActionCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TriggerActionCode",
                table: "OrderTypeTransitions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "OrderTypeRecipientActions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ActionCode",
                table: "OrderTypeRecipientActions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "OrderActionPermissions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ActionCode",
                table: "OrderActionPermissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "OrderActionLogs",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ActionCode",
                table: "OrderActionLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // XD01-366: every action column gets its code next to its number. The numbers are the closed enumeration's
            // (OrderActionAliases) and stay; the codes are backfilled from them, so every existing row reads identically.
            // A number no action ever had is left without a code (and keeps reading as it did: not at all).
            migrationBuilder.Sql(
                """
                UPDATE "OrderActionLogs" SET "ActionCode" = CASE "Action"
                     WHEN 0 THEN 'View' WHEN 1 THEN 'Approve' WHEN 2 THEN 'Reject' WHEN 3 THEN 'Assign'
                     WHEN 4 THEN 'Accept' WHEN 5 THEN 'Dispatch' WHEN 6 THEN 'Execute' WHEN 7 THEN 'Complete'
                     WHEN 8 THEN 'Cancel' WHEN 9 THEN 'Annotate' WHEN 10 THEN 'Calculate' WHEN 11 THEN 'Validate'
                     WHEN 12 THEN 'FlagClandestine' WHEN 13 THEN 'Dispute'
                 END
                 WHERE "ActionCode" IS NULL AND "Action" BETWEEN 0 AND 13;

                UPDATE "OrderActionPermissions" SET "ActionCode" = CASE "Action"
                     WHEN 0 THEN 'View' WHEN 1 THEN 'Approve' WHEN 2 THEN 'Reject' WHEN 3 THEN 'Assign'
                     WHEN 4 THEN 'Accept' WHEN 5 THEN 'Dispatch' WHEN 6 THEN 'Execute' WHEN 7 THEN 'Complete'
                     WHEN 8 THEN 'Cancel' WHEN 9 THEN 'Annotate' WHEN 10 THEN 'Calculate' WHEN 11 THEN 'Validate'
                     WHEN 12 THEN 'FlagClandestine' WHEN 13 THEN 'Dispute'
                 END
                 WHERE "ActionCode" IS NULL AND "Action" BETWEEN 0 AND 13;

                UPDATE "OrderTypeRecipientActions" SET "ActionCode" = CASE "Action"
                     WHEN 0 THEN 'View' WHEN 1 THEN 'Approve' WHEN 2 THEN 'Reject' WHEN 3 THEN 'Assign'
                     WHEN 4 THEN 'Accept' WHEN 5 THEN 'Dispatch' WHEN 6 THEN 'Execute' WHEN 7 THEN 'Complete'
                     WHEN 8 THEN 'Cancel' WHEN 9 THEN 'Annotate' WHEN 10 THEN 'Calculate' WHEN 11 THEN 'Validate'
                     WHEN 12 THEN 'FlagClandestine' WHEN 13 THEN 'Dispute'
                 END
                 WHERE "ActionCode" IS NULL AND "Action" BETWEEN 0 AND 13;

                UPDATE "OrderTypeTransitions" SET "TriggerActionCode" = CASE "TriggerAction"
                     WHEN 0 THEN 'View' WHEN 1 THEN 'Approve' WHEN 2 THEN 'Reject' WHEN 3 THEN 'Assign'
                     WHEN 4 THEN 'Accept' WHEN 5 THEN 'Dispatch' WHEN 6 THEN 'Execute' WHEN 7 THEN 'Complete'
                     WHEN 8 THEN 'Cancel' WHEN 9 THEN 'Annotate' WHEN 10 THEN 'Calculate' WHEN 11 THEN 'Validate'
                     WHEN 12 THEN 'FlagClandestine' WHEN 13 THEN 'Dispute'
                 END
                 WHERE "TriggerActionCode" IS NULL AND "TriggerAction" BETWEEN 0 AND 13;
                """);

            // The recipient insert policy compares codes now (XD01-366). Strictly: an entry that carries a code is granted
            // only by a grant with that code; only an entry with no code — written by a writer that predates this
            // migration — is compared by number. A code and a number can never be mixed to pass a grant, and a grant row
            // without a code (written the same way) refuses a coded entry: closed, never open. The two-argument function
            // stays for that older writer's sake until the number columns are dropped.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION service_order_recipient_action_is_granted(
                    p_order_id character varying, p_action integer, p_action_code character varying)
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
                                 AND CASE
                                         WHEN p_action_code IS NOT NULL THEN a."ActionCode" = p_action_code
                                         ELSE a."Action" = p_action
                                     END
                           );
                $$;

                DROP POLICY dispatch_recipient_insert_policy ON "OrderActionLogs";

                CREATE POLICY dispatch_recipient_insert_policy ON "OrderActionLogs"
                    AS PERMISSIVE
                    FOR INSERT
                    WITH CHECK (
                        service_order_recipient_action_is_granted("ServiceOrderId", "Action", "ActionCode")
                        OR service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 2)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // XD01-366: going back to number-only columns would lose every action that has no historical number (and EF
            // would rewrite their NULL numbers to 0, i.e. "View"). Refuse while any row holds one.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OrderActionLogs" WHERE "Action" IS NULL)
                       OR EXISTS (SELECT 1 FROM "OrderActionPermissions" WHERE "Action" IS NULL)
                       OR EXISTS (SELECT 1 FROM "OrderTypeRecipientActions" WHERE "Action" IS NULL)
                       OR EXISTS (SELECT 1 FROM "OrderTypeTransitions" WHERE "TriggerAction" IS NULL AND "TriggerActionCode" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'AddOrderActionCodes cannot be reverted: rows hold order actions that have no number.';
                    END IF;
                END $$;

                DROP POLICY dispatch_recipient_insert_policy ON "OrderActionLogs";

                CREATE POLICY dispatch_recipient_insert_policy ON "OrderActionLogs"
                    AS PERMISSIVE
                    FOR INSERT
                    WITH CHECK (
                        service_order_recipient_action_is_granted("ServiceOrderId", "Action")
                        OR service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 2)
                    );

                DROP FUNCTION service_order_recipient_action_is_granted(character varying, integer, character varying);
                """);

            migrationBuilder.DropColumn(
                name: "TriggerActionCode",
                table: "OrderTypeTransitions");

            migrationBuilder.DropColumn(
                name: "ActionCode",
                table: "OrderTypeRecipientActions");

            migrationBuilder.DropColumn(
                name: "ActionCode",
                table: "OrderActionPermissions");

            migrationBuilder.DropColumn(
                name: "ActionCode",
                table: "OrderActionLogs");

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "OrderTypeRecipientActions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "OrderActionPermissions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Action",
                table: "OrderActionLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
