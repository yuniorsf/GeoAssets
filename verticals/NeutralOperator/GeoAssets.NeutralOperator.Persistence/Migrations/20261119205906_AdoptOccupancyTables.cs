using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.NeutralOperator.Persistence.Migrations
{
    /// <summary>
    /// Baseline of the NeutralOperator chain (XD01-380): <b>adopts</b> the three occupancy tables the kernel's
    /// ServiceOrderDbContext chains created in <c>public</c> (GeoAssets.Server's and GeoAssets.FieldService.Server's
    /// AddPoleOccupancies*, AddServiceOrderOccupancyChangeSets*), moving them into schema <c>neutral_operator</c> with
    /// <c>ALTER TABLE … SET SCHEMA</c> — rows, indexes, constraints (the composite entry → pole FK and the change set →
    /// <c>public."ServiceOrderTargets"</c> FK alike), the ENABLE ROW LEVEL SECURITY flag and the row-level-security
    /// policies all travel with the table; nothing is copied, nothing is dropped. Hand-written SQL in place of the
    /// generated CreateTable calls (the designer and snapshot are the generated ones), idempotent and guarded:
    /// <list type="bullet">
    /// <item><description>table in <c>public</c> only (every database the kernel chain has migrated): moved;</description></item>
    /// <item><description>table in <c>neutral_operator</c> only (already adopted — a re-run): left as it is;</description></item>
    /// <item><description>
    /// table in neither (a database the kernel chain never created it in): created in <c>neutral_operator</c> with the
    /// kernel migrations' DDL — which needs the kernel's <c>public."ServiceOrderTargets"</c>, so it refuses to run before
    /// the kernel chain;
    /// </description></item>
    /// <item><description>table in both: refuses (raises) — never guesses which copy holds the data.</description></item>
    /// </list>
    /// Then, whichever way the table got here, row-level security is enabled and each table's
    /// <c>tenant_isolation_policy</c> created when it is missing, with the SQL of AddPoleOccupanciesRowLevelSecurity and
    /// AddServiceOrderOccupancyChangeSetsRowLevelSecurity (the change set's WITH CHECK schema-qualifies the kernel table; a
    /// moved policy keeps resolving it, since PostgreSQL stores the policy bound to the table, not to its name). No policy
    /// of AddServiceOrderDispatchRecipientRowLevelSecurity is on these tables. Apply after the kernel chain.
    /// </summary>
    public partial class AdoptOccupancyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS neutral_operator;

                DO $adopt$
                DECLARE
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['field_pole_occupancies', 'field_pole_occupancy_entries', 'ServiceOrderOccupancyChangeSets']
                    LOOP
                        IF to_regclass(format('neutral_operator.%I', t)) IS NOT NULL THEN
                            IF to_regclass(format('public.%I', t)) IS NOT NULL THEN
                                RAISE EXCEPTION 'Table % exists in both public and neutral_operator; resolve it by hand before adopting (nothing was changed).', t;
                            END IF;
                        ELSIF to_regclass(format('public.%I', t)) IS NOT NULL THEN
                            EXECUTE format('ALTER TABLE public.%I SET SCHEMA neutral_operator', t);
                        END IF;
                    END LOOP;

                    IF to_regclass('neutral_operator.field_pole_occupancies') IS NULL
                        OR to_regclass('neutral_operator.field_pole_occupancy_entries') IS NULL
                        OR to_regclass('neutral_operator."ServiceOrderOccupancyChangeSets"') IS NULL
                    THEN
                        IF to_regclass('public."ServiceOrderTargets"') IS NULL THEN
                            RAISE EXCEPTION 'public."ServiceOrderTargets" is missing: apply the kernel ServiceOrderDbContext migrations before the NeutralOperator ones.';
                        END IF;
                    END IF;
                END
                $adopt$;

                CREATE TABLE IF NOT EXISTS neutral_operator.field_pole_occupancies (
                    "Id" uuid NOT NULL,
                    "PoleFeatureId" character varying(64) COLLATE "C" NOT NULL,
                    "OrganizationId" uuid NOT NULL,
                    "Version" integer NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_field_pole_occupancies" PRIMARY KEY ("Id"),
                    CONSTRAINT "AK_field_pole_occupancies_Id_OrganizationId" UNIQUE ("Id", "OrganizationId")
                );

                CREATE TABLE IF NOT EXISTS neutral_operator.field_pole_occupancy_entries (
                    "Id" uuid NOT NULL,
                    "PoleOccupancyId" uuid NOT NULL,
                    "SlotIndex" integer,
                    "Status" character varying(32) NOT NULL,
                    "OccupantOrganizationId" uuid,
                    "OperatorCnpj" character varying(18),
                    "AnatelLicenseNumber" character varying(64),
                    "CableDiameterMm" numeric(8,3) NOT NULL,
                    "CableLoadDaN" numeric(12,3) NOT NULL,
                    "AttachedCableFeatureId" character varying(64),
                    "OrganizationId" uuid NOT NULL,
                    CONSTRAINT "PK_field_pole_occupancy_entries" PRIMARY KEY ("Id"),
                    CONSTRAINT "CK_field_pole_occupancy_entries_SlotIndex" CHECK ("SlotIndex" IS NULL OR "SlotIndex" BETWEEN 1 AND 6),
                    CONSTRAINT "FK_field_pole_occupancy_entries_field_pole_occupancies" FOREIGN KEY ("PoleOccupancyId", "OrganizationId")
                        REFERENCES neutral_operator.field_pole_occupancies ("Id", "OrganizationId") ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS neutral_operator."ServiceOrderOccupancyChangeSets" (
                    "Id" uuid NOT NULL,
                    "ServiceOrderId" character varying(36) NOT NULL,
                    "TargetId" character varying(36) COLLATE "C" NOT NULL,
                    "PoleFeatureId" character varying(64) COLLATE "C" NOT NULL,
                    "ChangesJson" text NOT NULL,
                    "BaselineVersion" integer NOT NULL,
                    "Status" character varying(16) NOT NULL,
                    "StagedBy" character varying(256) NOT NULL,
                    "StagedAt" timestamp with time zone NOT NULL,
                    "ClosedAt" timestamp with time zone,
                    "OrganizationId" uuid NOT NULL,
                    CONSTRAINT "PK_ServiceOrderOccupancyChangeSets" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ServiceOrderOccupancyChangeSets_ServiceOrderTargets_TargetId" FOREIGN KEY ("TargetId")
                        REFERENCES public."ServiceOrderTargets" ("Id") ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_field_pole_occupancies_OrganizationId_PoleFeatureId"
                    ON neutral_operator.field_pole_occupancies ("OrganizationId", "PoleFeatureId");
                CREATE INDEX IF NOT EXISTS "IX_field_pole_occupancies_PoleFeatureId"
                    ON neutral_operator.field_pole_occupancies ("PoleFeatureId");
                CREATE INDEX IF NOT EXISTS "IX_field_pole_occupancy_entries_OccupantOrganizationId"
                    ON neutral_operator.field_pole_occupancy_entries ("OccupantOrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_field_pole_occupancy_entries_OrganizationId"
                    ON neutral_operator.field_pole_occupancy_entries ("OrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_field_pole_occupancy_entries_PoleOccupancyId_OrganizationId"
                    ON neutral_operator.field_pole_occupancy_entries ("PoleOccupancyId", "OrganizationId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_field_pole_occupancy_entries_PoleOccupancyId_SlotIndex"
                    ON neutral_operator.field_pole_occupancy_entries ("PoleOccupancyId", "SlotIndex") WHERE "SlotIndex" IS NOT NULL;
                CREATE INDEX IF NOT EXISTS "IX_ServiceOrderOccupancyChangeSets_OrganizationId"
                    ON neutral_operator."ServiceOrderOccupancyChangeSets" ("OrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_ServiceOrderOccupancyChangeSets_ServiceOrderId_Status"
                    ON neutral_operator."ServiceOrderOccupancyChangeSets" ("ServiceOrderId", "Status");
                CREATE INDEX IF NOT EXISTS "IX_ServiceOrderOccupancyChangeSets_TargetId"
                    ON neutral_operator."ServiceOrderOccupancyChangeSets" ("TargetId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServiceOrderOccupancyChangeSets_TargetId_Staged"
                    ON neutral_operator."ServiceOrderOccupancyChangeSets" ("TargetId") WHERE "Status" = 'Staged';

                ALTER TABLE neutral_operator.field_pole_occupancies ENABLE ROW LEVEL SECURITY;
                ALTER TABLE neutral_operator.field_pole_occupancy_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE neutral_operator."ServiceOrderOccupancyChangeSets" ENABLE ROW LEVEL SECURITY;

                DO $policies$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'neutral_operator'
                                   AND tablename = 'field_pole_occupancies' AND policyname = 'tenant_isolation_policy') THEN
                        CREATE POLICY tenant_isolation_policy ON neutral_operator.field_pole_occupancies
                            AS PERMISSIVE
                            FOR ALL
                            USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                            WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'neutral_operator'
                                   AND tablename = 'field_pole_occupancy_entries' AND policyname = 'tenant_isolation_policy') THEN
                        CREATE POLICY tenant_isolation_policy ON neutral_operator.field_pole_occupancy_entries
                            AS PERMISSIVE
                            FOR ALL
                            USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                            WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'neutral_operator'
                                   AND tablename = 'ServiceOrderOccupancyChangeSets' AND policyname = 'tenant_isolation_policy') THEN
                        CREATE POLICY tenant_isolation_policy ON neutral_operator."ServiceOrderOccupancyChangeSets"
                            AS PERMISSIVE
                            FOR ALL
                            USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                            WITH CHECK (
                                "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                                AND EXISTS (
                                    SELECT 1 FROM public."ServiceOrderTargets" t
                                    WHERE t."Id" = "ServiceOrderOccupancyChangeSets"."TargetId"
                                      AND t."ServiceOrderId" = "ServiceOrderOccupancyChangeSets"."ServiceOrderId"
                                      AND t."OrganizationId" = "ServiceOrderOccupancyChangeSets"."OrganizationId"
                                )
                            );
                    END IF;
                END
                $policies$;
                """);
        }

        /// <summary>
        /// Gives the tables back to <c>public</c> — the place the kernel chains' own migrations know them in — by the same
        /// <c>ALTER TABLE … SET SCHEMA</c>, rows, constraints and policies included. Never drops a table: refuses (raises)
        /// when <c>public</c> already holds a table of the same name, and leaves a table that is not in
        /// <c>neutral_operator</c> alone. The schema stays — it holds this chain's history table.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $release$
                DECLARE
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['ServiceOrderOccupancyChangeSets', 'field_pole_occupancy_entries', 'field_pole_occupancies']
                    LOOP
                        IF to_regclass(format('neutral_operator.%I', t)) IS NOT NULL THEN
                            IF to_regclass(format('public.%I', t)) IS NOT NULL THEN
                                RAISE EXCEPTION 'Table % exists in both public and neutral_operator; resolve it by hand before releasing (nothing was changed).', t;
                            END IF;
                            EXECUTE format('ALTER TABLE neutral_operator.%I SET SCHEMA public', t);
                        END IF;
                    END LOOP;
                END
                $release$;
                """);
        }
    }
}
