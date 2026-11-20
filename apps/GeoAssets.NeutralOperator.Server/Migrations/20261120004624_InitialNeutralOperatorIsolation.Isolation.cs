namespace GeoAssets.NeutralOperator.Server.Migrations
{
    /// <summary>
    /// The part of this host's initial schema that the EF model cannot express (XD01-382): the triggers and the row-level
    /// security of the generic workflow tables, in the final state the <c>GeoAssets.FieldService.Server</c> history — the
    /// other isolated vertical database — reaches them, restricted to the kernel <c>ServiceOrderDbContext</c>'s tables
    /// (the vertical tables are <c>NeutralOperatorDbContext</c>'s, with their own policies in its own chain):
    /// <list type="bullet">
    ///   <item>the <c>"ServiceOrders"."RowVersion"</c> trigger (AddServiceOrderRowVersionTrigger) and the
    ///   <c>"ServiceOrderTargets"."KernelOrderId"</c> trigger (AddTargetLedgerVertical);</item>
    ///   <item>tenant isolation: own organization, or an active, unexpired, <c>ServiceOrder</c>-typed (or untyped) grant
    ///   whose <c>ScopeId</c>, when set, equals the order's <c>scopeId</c> attribute — read from the local
    ///   <c>"OrganizationGrantMirror"</c> (AddIsolatedHistoryRowLevelSecurity, AddServiceOrderTargetsRowLevelSecurity);</item>
    ///   <item>dispatch recipients (AddServiceOrderDispatchRecipientRowLevelSecurity, AddOrderTypeRecipientAccess,
    ///   AddOrderTypeRecipientActions, AddOrderActionCodes), with the shared-use occupant notice's recipient actions;</item>
    ///   <item>the ledger's vertical isolation of <c>"ServiceOrderTargets"</c> (AddTargetLedgerVertical).</item>
    /// </list>
    /// Like every RLS migration in this repository it omits <c>FORCE ROW LEVEL SECURITY</c>: every connection still runs
    /// as the table owner. Nothing populates <c>"OrganizationGrantMirror"</c> on this host yet (no grant sync is wired),
    /// so the database grants nothing across organizations — less than the kernel would allow, never more.
    /// </summary>
    public partial class InitialNeutralOperatorIsolation
    {
        private const string IsolationUp =
            """
            CREATE OR REPLACE FUNCTION touch_service_orders_row_version() RETURNS trigger AS $$
            BEGIN
                NEW."RowVersion" := uuid_send(gen_random_uuid());
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER service_orders_row_version_touch
            BEFORE INSERT OR UPDATE ON "ServiceOrders"
            FOR EACH ROW EXECUTE FUNCTION touch_service_orders_row_version();

            CREATE FUNCTION service_order_targets_set_kernel_order_id()
            RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = public, pg_temp
            AS $$
            BEGIN
                NEW."KernelOrderId" := CASE WHEN NEW."VerticalId" = 'kernel' THEN NEW."ServiceOrderId" END;
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER service_order_targets_kernel_order_id
                BEFORE INSERT OR UPDATE OF "VerticalId", "ServiceOrderId", "KernelOrderId" ON "ServiceOrderTargets"
                FOR EACH ROW
                EXECUTE FUNCTION service_order_targets_set_kernel_order_id();

            ALTER TABLE "ServiceOrders" ENABLE ROW LEVEL SECURITY;

            CREATE POLICY tenant_isolation_policy ON "ServiceOrders"
                AS PERMISSIVE
                FOR ALL
                USING (
                    "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                    OR EXISTS (
                        SELECT 1 FROM "OrganizationGrantMirror" g
                        WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                          AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                          AND g."IsActive"
                          AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                          AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                          AND (g."ScopeId" IS NULL OR g."ScopeId" = ("ServiceOrders"."AttributesJson"::jsonb ->> 'scopeId'))
                    )
                )
                WITH CHECK (
                    "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                    OR EXISTS (
                        SELECT 1 FROM "OrganizationGrantMirror" g
                        WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                          AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                          AND g."IsActive"
                          AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                          AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                          AND (g."ScopeId" IS NULL OR g."ScopeId" = ("ServiceOrders"."AttributesJson"::jsonb ->> 'scopeId'))
                    )
                );

            ALTER TABLE "ServiceOrderTargets" ENABLE ROW LEVEL SECURITY;

            CREATE POLICY tenant_isolation_policy ON "ServiceOrderTargets"
                AS PERMISSIVE
                FOR ALL
                USING (
                    "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                    OR EXISTS (
                        SELECT 1 FROM "ServiceOrders" o
                        JOIN "OrganizationGrantMirror" g ON g."ResourceOrganizationId" = o."OrganizationId"
                        WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                          AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                          AND g."IsActive"
                          AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                          AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                          AND (g."ScopeId" IS NULL OR g."ScopeId" = (o."AttributesJson"::jsonb ->> 'scopeId'))
                    )
                )
                WITH CHECK (
                    EXISTS (
                        SELECT 1 FROM "ServiceOrders" o
                        WHERE o."Id" = "ServiceOrderTargets"."ServiceOrderId"
                          AND o."OrganizationId" = "ServiceOrderTargets"."OrganizationId"
                          AND (
                              o."OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              OR EXISTS (
                                  SELECT 1 FROM "OrganizationGrantMirror" g
                                  WHERE g."ResourceOrganizationId" = o."OrganizationId"
                                    AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                                    AND g."IsActive"
                                    AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                                    AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                                    AND (g."ScopeId" IS NULL OR g."ScopeId" = (o."AttributesJson"::jsonb ->> 'scopeId'))
                              )
                          )
                    )
                );

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
                                SELECT 1 FROM "OrganizationGrantMirror" g
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

            CREATE POLICY dispatch_recipient_select_policy ON "ServiceOrders"
                AS PERMISSIVE
                FOR SELECT
                USING (service_order_dispatched_to_current_organization("Id"));

            CREATE POLICY dispatch_recipient_update_policy ON "ServiceOrders"
                AS PERMISSIVE
                FOR UPDATE
                USING (service_order_dispatched_to_current_organization_with_access("Id", 2))
                WITH CHECK (service_order_dispatched_to_current_organization_with_access("Id", 2));

            CREATE POLICY dispatch_recipient_action_update_policy ON "ServiceOrders"
                AS PERMISSIVE
                FOR UPDATE
                USING (service_order_recipient_actions_granted("Id"))
                WITH CHECK (service_order_recipient_change_is_granted("ServiceOrders"));

            CREATE POLICY organization_is_fixed_policy ON "ServiceOrders"
                AS RESTRICTIVE
                FOR UPDATE
                USING (true)
                WITH CHECK (service_order_keeps_organization("Id", "OrganizationId"));

            CREATE POLICY dispatch_recipient_select_policy ON "ServiceOrderTargets"
                AS PERMISSIVE
                FOR SELECT
                USING (service_order_dispatched_to_current_organization("ServiceOrderId"));

            CREATE POLICY dispatch_recipient_update_policy ON "ServiceOrderTargets"
                AS PERMISSIVE
                FOR UPDATE
                USING (service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 1))
                WITH CHECK (
                    service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 1)
                    AND service_order_target_stays_on_order("Id", "ServiceOrderId")
                    AND service_order_keeps_organization("ServiceOrderId", "OrganizationId")
                );

            CREATE POLICY ledger_vertical_isolation_policy ON "ServiceOrderTargets"
                AS RESTRICTIVE
                FOR ALL
                USING (
                    "VerticalId" = 'kernel'
                    OR "OrganizationId" = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                )
                WITH CHECK (
                    "VerticalId" = 'kernel'
                    OR "OrganizationId" = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                );

            CREATE POLICY ledger_vertical_tenant_policy ON "ServiceOrderTargets"
                AS PERMISSIVE
                FOR ALL
                USING (
                    "VerticalId" <> 'kernel'
                    AND "OrganizationId" = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                )
                WITH CHECK (
                    "VerticalId" <> 'kernel'
                    AND "OrganizationId" = NULLIF(current_setting('app.current_organization_id', true), '')::uuid
                );

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
                    service_order_recipient_action_is_granted("ServiceOrderId", "Action", "ActionCode")
                    OR service_order_dispatched_to_current_organization_with_access("ServiceOrderId", 2)
                );

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

            INSERT INTO "OrderTypeRecipientActions" ("OrderTypeId", "Action", "ActionCode", "FromStateKey", "ToStateKey") VALUES
                ('shared-use-occupant-notice', 4, 'Accept', NULL, NULL),
                ('shared-use-occupant-notice', 4, 'Accept', 'Pending', 'InProgress'),
                ('shared-use-occupant-notice', 9, 'Annotate', NULL, NULL);
            """;

        private const string IsolationDown =
            """
            DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "OrderDispatches";
            DROP POLICY IF EXISTS tenant_isolation_policy ON "OrderDispatches";
            DROP POLICY IF EXISTS dispatch_recipient_insert_policy ON "OrderActionLogs";
            DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "OrderActionLogs";
            DROP POLICY IF EXISTS tenant_isolation_policy ON "OrderActionLogs";
            DROP POLICY IF EXISTS ledger_vertical_tenant_policy ON "ServiceOrderTargets";
            DROP POLICY IF EXISTS ledger_vertical_isolation_policy ON "ServiceOrderTargets";
            DROP POLICY IF EXISTS dispatch_recipient_update_policy ON "ServiceOrderTargets";
            DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "ServiceOrderTargets";
            DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrderTargets";
            DROP POLICY IF EXISTS organization_is_fixed_policy ON "ServiceOrders";
            DROP POLICY IF EXISTS dispatch_recipient_action_update_policy ON "ServiceOrders";
            DROP POLICY IF EXISTS dispatch_recipient_update_policy ON "ServiceOrders";
            DROP POLICY IF EXISTS dispatch_recipient_select_policy ON "ServiceOrders";
            DROP POLICY IF EXISTS tenant_isolation_policy ON "ServiceOrders";

            DROP FUNCTION IF EXISTS service_order_recipient_change_is_granted("ServiceOrders");
            DROP FUNCTION IF EXISTS service_order_recipient_action_is_granted(character varying, integer, character varying);
            DROP FUNCTION IF EXISTS service_order_recipient_actions_granted(character varying);
            DROP FUNCTION IF EXISTS service_order_target_stays_on_order(character varying, character varying);
            DROP FUNCTION IF EXISTS service_order_dispatched_to_current_organization_with_access(character varying, integer);
            DROP FUNCTION IF EXISTS service_order_keeps_organization(character varying, uuid);
            DROP FUNCTION IF EXISTS service_order_owned_by_or_granted_to_current_organization(character varying);
            DROP FUNCTION IF EXISTS service_order_dispatched_to_current_organization(character varying);

            DROP TRIGGER IF EXISTS service_order_targets_kernel_order_id ON "ServiceOrderTargets";
            DROP FUNCTION IF EXISTS service_order_targets_set_kernel_order_id();
            DROP TRIGGER IF EXISTS service_orders_row_version_touch ON "ServiceOrders";
            DROP FUNCTION IF EXISTS touch_service_orders_row_version();
            """;
    }
}
