using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Persistence.Migrations
{
    /// <summary>
    /// Baseline of the FieldService chain (XD01-381): <b>adopts</b> the five tables the kernel's ServiceOrderDbContext chain of
    /// GeoAssets.FieldService.Server created in <c>public</c> (InitialFieldServiceIsolation, with the row-level security of
    /// AddIsolatedHistoryRowLevelSecurity and AddOrderChildTablesRowLevelSecurity) — <c>"ContractorUsLedgerEntries"</c>,
    /// <c>"UsCatalogItems"</c>, <c>field_contractor_us_postings</c>, <c>field_icerp_paving_measurements</c> and
    /// <c>field_manhole_audits</c> — moving them into schema <c>field_service</c> with <c>ALTER TABLE … SET SCHEMA</c>: rows,
    /// indexes, constraints (the postings' and paving measurements' foreign keys to <c>public."ServiceOrders"</c> included),
    /// the ENABLE ROW LEVEL SECURITY flag and the row-level-security policies all travel with the table; nothing is copied,
    /// nothing is dropped. Hand-written SQL in place of the generated CreateTable calls (the designer and snapshot are the
    /// generated ones), idempotent and guarded:
    /// <list type="bullet">
    /// <item><description>table in <c>public</c> only (every FieldService database the kernel chain has migrated): moved;</description></item>
    /// <item><description>table in <c>field_service</c> only (already adopted — a re-run): left as it is;</description></item>
    /// <item><description>
    /// table in neither (a database the kernel chain never created it in): created in <c>field_service</c> with the kernel
    /// migrations' DDL, foreign keys to <c>public."ServiceOrders"</c> included (database-only: this context's model does not
    /// map the kernel's orders) — so it refuses to run before the kernel chain;
    /// </description></item>
    /// <item><description>table in both: refuses (raises) — never guesses which copy holds the data.</description></item>
    /// </list>
    /// Then, whichever way the table got here, row-level security is enabled and each table's
    /// <c>tenant_isolation_policy</c> created when it is missing, with the SQL of the FieldService chain (owner-only
    /// equality on <c>"OrganizationId"</c>, on <c>"ContractorOrganizationId"</c> for the postings). None of these policies
    /// reads another table, and the tables have no trigger and no helper function, so nothing they use needs re-qualifying
    /// after the move. Apply after the kernel chain (and, by convention, after the NeutralOperator chain).
    /// </summary>
    public partial class AdoptFieldServiceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS field_service;

                DO $adopt$
                DECLARE
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['ContractorUsLedgerEntries', 'UsCatalogItems', 'field_contractor_us_postings', 'field_icerp_paving_measurements', 'field_manhole_audits']
                    LOOP
                        IF to_regclass(format('field_service.%I', t)) IS NOT NULL THEN
                            IF to_regclass(format('public.%I', t)) IS NOT NULL THEN
                                RAISE EXCEPTION 'Table % exists in both public and field_service; resolve it by hand before adopting (nothing was changed).', t;
                            END IF;
                        ELSIF to_regclass(format('public.%I', t)) IS NOT NULL THEN
                            EXECUTE format('ALTER TABLE public.%I SET SCHEMA field_service', t);
                        END IF;
                    END LOOP;

                    IF to_regclass('field_service."ContractorUsLedgerEntries"') IS NULL
                        OR to_regclass('field_service."UsCatalogItems"') IS NULL
                        OR to_regclass('field_service.field_contractor_us_postings') IS NULL
                        OR to_regclass('field_service.field_icerp_paving_measurements') IS NULL
                        OR to_regclass('field_service.field_manhole_audits') IS NULL
                    THEN
                        IF to_regclass('public."ServiceOrders"') IS NULL THEN
                            RAISE EXCEPTION 'public."ServiceOrders" is missing: apply the kernel ServiceOrderDbContext migrations before the FieldService ones.';
                        END IF;
                    END IF;
                END
                $adopt$;

                CREATE TABLE IF NOT EXISTS field_service."ContractorUsLedgerEntries" (
                    "Id" uuid NOT NULL,
                    "OrganizationId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                    "ServiceOrderId" character varying(36),
                    "UsCode" character varying(64) NOT NULL,
                    "Description" character varying(4096) NOT NULL,
                    "Quantity" numeric(18,4) NOT NULL,
                    "UnitValue" numeric(18,4) NOT NULL,
                    "PhotoProofJson" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    CONSTRAINT "PK_ContractorUsLedgerEntries" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS field_service."UsCatalogItems" (
                    "Id" uuid NOT NULL,
                    "OrganizationId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                    "ItemCode" character varying(64) NOT NULL,
                    "Description" character varying(4096) NOT NULL,
                    "Unit" character varying(32) NOT NULL,
                    "BaseUsFactor" numeric(18,6) NOT NULL,
                    "RegionalMultiplier" numeric(18,6) NOT NULL DEFAULT 1.0,
                    "MonetaryConversionRate" numeric(18,6) NOT NULL,
                    "EffectiveFrom" timestamp with time zone NOT NULL,
                    "EffectiveTo" timestamp with time zone,
                    CONSTRAINT "PK_UsCatalogItems" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS field_service.field_contractor_us_postings (
                    "Id" uuid NOT NULL,
                    "ServiceOrderId" character varying(36) NOT NULL,
                    "ContractorOrganizationId" uuid NOT NULL,
                    "ItemCode" character varying(64) NOT NULL,
                    "Quantity" numeric(18,4) NOT NULL,
                    "UnitUs" numeric(18,6) NOT NULL,
                    "TotalUs" numeric(18,6) NOT NULL,
                    "MonetaryRate" numeric(18,6) NOT NULL,
                    "TotalBrl" numeric(18,2) NOT NULL,
                    "PostedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_field_contractor_us_postings" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_field_contractor_us_postings_ServiceOrders_ServiceOrderId" FOREIGN KEY ("ServiceOrderId")
                        REFERENCES public."ServiceOrders" ("Id") ON DELETE RESTRICT
                );

                CREATE TABLE IF NOT EXISTS field_service.field_icerp_paving_measurements (
                    "Id" uuid NOT NULL,
                    "ParentServiceOrderId" character varying(36) NOT NULL,
                    "ChildServiceOrderId" character varying(36) NOT NULL,
                    "OrganizationId" uuid NOT NULL,
                    "LengthMeters" double precision NOT NULL,
                    "WidthMeters" double precision NOT NULL,
                    "DepthMeters" double precision NOT NULL,
                    "RecordedAreaSquareMeters" double precision NOT NULL,
                    "ActualAreaSquareMeters" double precision NOT NULL,
                    "VolumeCubicMeters" double precision NOT NULL,
                    "RecordedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_field_icerp_paving_measurements" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_field_icerp_paving_measurements_ServiceOrders_Child" FOREIGN KEY ("ChildServiceOrderId")
                        REFERENCES public."ServiceOrders" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_field_icerp_paving_measurements_ServiceOrders_Parent" FOREIGN KEY ("ParentServiceOrderId")
                        REFERENCES public."ServiceOrders" ("Id") ON DELETE RESTRICT
                );

                CREATE TABLE IF NOT EXISTS field_service.field_manhole_audits (
                    "Id" uuid NOT NULL,
                    "ConduitFeatureId" character varying(64) NOT NULL,
                    "OrganizationId" uuid NOT NULL,
                    "BoreOccupancyPercentage" double precision NOT NULL,
                    "SubDuctsJson" text NOT NULL,
                    "SpliceEnclosureCondition" character varying(256) NOT NULL,
                    "Nr33ChecklistAttributesJson" text NOT NULL,
                    "AuditedBy" character varying(128) NOT NULL,
                    "AuditedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_field_manhole_audits" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "IX_ContractorUsLedgerEntries_OrganizationId"
                    ON field_service."ContractorUsLedgerEntries" ("OrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_ContractorUsLedgerEntries_ServiceOrderId"
                    ON field_service."ContractorUsLedgerEntries" ("ServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_UsCatalogItems_OrganizationId_ItemCode"
                    ON field_service."UsCatalogItems" ("OrganizationId", "ItemCode");
                CREATE INDEX IF NOT EXISTS "IX_field_contractor_us_postings_ContractorOrganizationId_Poste~"
                    ON field_service.field_contractor_us_postings ("ContractorOrganizationId", "PostedAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_field_contractor_us_postings_ServiceOrderId"
                    ON field_service.field_contractor_us_postings ("ServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_field_icerp_paving_measurements_ChildServiceOrderId"
                    ON field_service.field_icerp_paving_measurements ("ChildServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_field_icerp_paving_measurements_OrganizationId"
                    ON field_service.field_icerp_paving_measurements ("OrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_field_icerp_paving_measurements_ParentServiceOrderId"
                    ON field_service.field_icerp_paving_measurements ("ParentServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_field_manhole_audits_ConduitFeatureId"
                    ON field_service.field_manhole_audits ("ConduitFeatureId");
                CREATE INDEX IF NOT EXISTS "IX_field_manhole_audits_OrganizationId"
                    ON field_service.field_manhole_audits ("OrganizationId");

                ALTER TABLE field_service."ContractorUsLedgerEntries" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE field_service."UsCatalogItems" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE field_service.field_contractor_us_postings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE field_service.field_icerp_paving_measurements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE field_service.field_manhole_audits ENABLE ROW LEVEL SECURITY;

                DO $policies$
                DECLARE
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['ContractorUsLedgerEntries', 'UsCatalogItems', 'field_icerp_paving_measurements', 'field_manhole_audits']
                    LOOP
                        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'field_service'
                                       AND tablename = t AND policyname = 'tenant_isolation_policy') THEN
                            EXECUTE format(
                                $policy$CREATE POLICY tenant_isolation_policy ON field_service.%I
                                    AS PERMISSIVE
                                    FOR ALL
                                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)$policy$,
                                t);
                        END IF;
                    END LOOP;

                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'field_service'
                                   AND tablename = 'field_contractor_us_postings' AND policyname = 'tenant_isolation_policy') THEN
                        CREATE POLICY tenant_isolation_policy ON field_service.field_contractor_us_postings
                            AS PERMISSIVE
                            FOR ALL
                            USING ("ContractorOrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                            WITH CHECK ("ContractorOrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                    END IF;
                END
                $policies$;
                """);
        }

        /// <summary>
        /// Gives the tables back to <c>public</c> — the place the kernel chain's own migrations know them in — by the same
        /// <c>ALTER TABLE … SET SCHEMA</c>, rows, constraints and policies included. Never drops a table: refuses (raises)
        /// when <c>public</c> already holds a table of the same name, and leaves a table that is not in
        /// <c>field_service</c> alone. The schema stays — it holds this chain's history table.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $release$
                DECLARE
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['ContractorUsLedgerEntries', 'UsCatalogItems', 'field_contractor_us_postings', 'field_icerp_paving_measurements', 'field_manhole_audits']
                    LOOP
                        IF to_regclass(format('field_service.%I', t)) IS NOT NULL
                            AND to_regclass(format('public.%I', t)) IS NOT NULL
                        THEN
                            RAISE EXCEPTION 'Table % exists in both public and field_service; resolve it by hand before releasing (nothing was changed).', t;
                        END IF;
                    END LOOP;

                    FOREACH t IN ARRAY ARRAY['ContractorUsLedgerEntries', 'UsCatalogItems', 'field_contractor_us_postings', 'field_icerp_paving_measurements', 'field_manhole_audits']
                    LOOP
                        IF to_regclass(format('field_service.%I', t)) IS NOT NULL THEN
                            EXECUTE format('ALTER TABLE field_service.%I SET SCHEMA public', t);
                        END IF;
                    END LOOP;
                END
                $release$;
                """);
        }
    }
}
