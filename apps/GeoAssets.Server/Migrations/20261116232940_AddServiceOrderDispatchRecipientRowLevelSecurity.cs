using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderDispatchRecipientRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-layer half of "an organization sees the orders dispatched to it" (XD01-316) —
            // the twin of the dispatch-recipient condition of the organization gate
            // (OrderOrganizationGate, XD01-332) and of EFServiceOrderRepository.GetDispatchedToAsync's
            // inbox, as the grant clause of tenant_isolation_policy is the twin of CrossOrgGrantRule.
            // A shared-use order is fanned out into one child order per occupant organization; each
            // child stays the pole owner's and is dispatched to its occupant, which until now could
            // not read a row of it.
            //
            // Three functions, all STABLE SECURITY DEFINER with a pinned search_path. They run with
            // the table owner's privileges, so the rows they read are not filtered by the policies
            // that call them — which is what keeps a policy on "ServiceOrders" that looks at
            // "OrderDispatches", and policies on "OrderDispatches" that look at "ServiceOrders", from
            // recursing into each other. Each answers one yes/no question about the session's own
            // organization (app.current_organization_id) and one order id:
            //
            //   service_order_dispatched_to_current_organization(id)
            //       — a dispatch of that order names the session's organization (TargetType 2 =
            //         DispatchTargetType.Organization). The target id is compared as a uuid, so its
            //         letter case does not matter; a target id that is not a uuid never matches.
            //   service_order_owned_by_or_granted_to_current_organization(id)
            //       — exactly the condition of tenant_isolation_policy on "ServiceOrders": the order is
            //         the session's organization's own, or a grant covers it.
            //   service_order_keeps_organization(id, organization)
            //       — the stored order has that organization. Called from a WITH CHECK it reads the
            //         row as it was before the statement, so it is true only for a new row version
            //         that left "OrganizationId" as it was.
            //
            // "ServiceOrders"
            //   dispatch_recipient_select_policy / dispatch_recipient_update_policy — a recipient
            //   organization reads the order and may update it (accepting or annotating an order
            //   stamps it). It can neither insert nor delete one.
            //   organization_is_fixed_policy — RESTRICTIVE, FOR UPDATE: no session changes an order's
            //   "OrganizationId" (XD01-332 fixed it at creation in the application; nothing in the
            //   database did). Without it the recipient policy would let an occupant relabel an order
            //   dispatched to it as its own, through tenant_isolation_policy's WITH CHECK; the same
            //   was already open to a grantee.
            //
            // "ServiceOrderTargets"
            //   The policy so far followed its order — whoever saw the order could read and write its
            //   targets. A recipient now sees the order, and the targets of an order dispatched to an
            //   occupant are read-only for it: they are the pole owner's assets. So
            //   tenant_isolation_policy is restated on owner-or-grantee (unchanged for those two), and
            //   dispatch_recipient_select_policy gives the recipient SELECT and nothing else.
            //
            // "OrderDispatches"
            //   A dispatch row now decides who can see an order, so who may write one matters: only
            //   the order's owner or a grantee (tenant_isolation_policy). A recipient reads the
            //   dispatches of an order dispatched to it (dispatch_recipient_select_policy) and can add
            //   none — it cannot pass the order on to a third organization.
            //   This table had no row-level security in this history at all: any session could have
            //   inserted a dispatch naming itself. It is enabled here.
            //
            // field_pole_occupancies, field_pole_occupancy_entries and
            // "ServiceOrderOccupancyChangeSets" are untouched: owner-only, as XD01-314/315 decided.
            // "OrderActionLogs" still has no policy in this history (XD01-323's finding).
            //
            // No FORCE and no GRANT, as everywhere else in this history. EXECUTE on the functions is
            // PUBLIC's by default; a role subject to these policies needs nothing new.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION service_order_dispatched_to_current_organization(p_order_id character varying)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT EXISTS (
                        SELECT 1 FROM "OrderDispatches" d
                        WHERE d."ServiceOrderId" = p_order_id
                          AND d."TargetType" = 2
                          AND CASE
                                WHEN d."TargetId" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN d."TargetId"::uuid = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                                ELSE false
                              END
                    );
                $$;

                CREATE FUNCTION service_order_owned_by_or_granted_to_current_organization(p_order_id character varying)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT EXISTS (
                        SELECT 1 FROM "ServiceOrders" o
                        WHERE o."Id" = p_order_id
                          AND (
                                o."OrganizationId" = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                                OR EXISTS (
                                    SELECT 1 FROM "OrganizationGrants" g
                                    WHERE g."ResourceOrganizationId" = o."OrganizationId"
                                      AND g."GranteeOrganizationId" = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                                      AND g."IsActive"
                                      AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                                      AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                                      AND (g."ScopeId" IS NULL OR g."ScopeId" = (o."AttributesJson"::jsonb ->> 'scopeId'))
                                )
                              )
                    );
                $$;

                CREATE FUNCTION service_order_keeps_organization(p_order_id character varying, p_organization_id uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $$
                    SELECT EXISTS (
                        SELECT 1 FROM "ServiceOrders" o
                        WHERE o."Id" = p_order_id
                          AND o."OrganizationId" = p_organization_id
                    );
                $$;

                CREATE POLICY dispatch_recipient_select_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR SELECT
                    USING (service_order_dispatched_to_current_organization("Id"));

                CREATE POLICY dispatch_recipient_update_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR UPDATE
                    USING (service_order_dispatched_to_current_organization("Id"))
                    WITH CHECK (service_order_dispatched_to_current_organization("Id"));

                CREATE POLICY organization_is_fixed_policy ON "ServiceOrders"
                    AS RESTRICTIVE
                    FOR UPDATE
                    USING (true)
                    WITH CHECK (service_order_keeps_organization("Id", "OrganizationId"));

                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrderTargets";

                CREATE POLICY tenant_isolation_policy ON "ServiceOrderTargets"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR service_order_owned_by_or_granted_to_current_organization("ServiceOrderId")
                    )
                    WITH CHECK (
                        service_order_owned_by_or_granted_to_current_organization("ServiceOrderId")
                        AND EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                              AND o."OrganizationId" = "ServiceOrderTargets"."OrganizationId"
                        )
                    );

                CREATE POLICY dispatch_recipient_select_policy ON "ServiceOrderTargets"
                    AS PERMISSIVE
                    FOR SELECT
                    USING (service_order_dispatched_to_current_organization("ServiceOrderId"));

                ALTER TABLE "OrderDispatches" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "OrderDispatches"
                    AS PERMISSIVE
                    FOR ALL
                    USING (service_order_owned_by_or_granted_to_current_organization("ServiceOrderId"))
                    WITH CHECK (service_order_owned_by_or_granted_to_current_organization("ServiceOrderId"));

                CREATE POLICY dispatch_recipient_select_policy ON "OrderDispatches"
                    AS PERMISSIVE
                    FOR SELECT
                    USING (service_order_dispatched_to_current_organization("ServiceOrderId"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Puts every policy back as the previous migration left it and removes the functions;
            // no row is touched.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "OrderDispatches";
                DROP POLICY IF EXISTS tenant_isolation_policy ON "OrderDispatches";
                ALTER TABLE "OrderDispatches" DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "ServiceOrderTargets";
                DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrderTargets";

                CREATE POLICY tenant_isolation_policy ON "ServiceOrderTargets"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1 FROM "ServiceOrders" o
                            WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                              AND o."OrganizationId" = "ServiceOrderTargets"."OrganizationId"
                        )
                    );

                DROP POLICY IF EXISTS organization_is_fixed_policy ON "ServiceOrders";
                DROP POLICY IF EXISTS dispatch_recipient_update_policy ON "ServiceOrders";
                DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "ServiceOrders";

                DROP FUNCTION IF EXISTS service_order_keeps_organization(character varying, uuid);
                DROP FUNCTION IF EXISTS service_order_owned_by_or_granted_to_current_organization(character varying);
                DROP FUNCTION IF EXISTS service_order_dispatched_to_current_organization(character varying);
                """);
        }
    }
}
