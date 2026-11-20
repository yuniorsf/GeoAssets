using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.ServiceOrderDb
{
    /// <summary>
    /// XD01-381: drops the FieldService vertical's five tables — <c>"ContractorUsLedgerEntries"</c>, <c>"UsCatalogItems"</c>,
    /// <c>field_contractor_us_postings</c>, <c>field_icerp_paving_measurements</c>, <c>field_manhole_audits</c> — from the
    /// kernel's database, where this chain created them (AddContractorUsLedgerEntries, AddUsCatalogItems,
    /// AddContractorUsPostings, AddPavingMeasurements, AddManholeAudits and their row-level-security migrations) although
    /// only GeoAssets.FieldService.Server ever used them, against its own database. The kernel's ServiceOrderDbContext no
    /// longer maps them; the FieldService vertical's FieldServiceDbContext does, on the FieldService database.
    ///
    /// <para>Hand-written SQL in place of the scaffolded DropTable operations (the designer and snapshot are the generated
    /// ones), <b>guarded</b>: every one of the five tables that exists is checked first, and if any holds a row the migration
    /// raises — <c>Cannot drop &lt;table&gt; from the kernel database: it holds N row(s). Move them to the FieldService
    /// database first.</c> — before anything is dropped (and the migration's transaction rolls back). The count runs with
    /// <c>row_security</c> off, so a row hidden by a row-level-security policy makes it fail loudly instead of counting as
    /// "no row". Only then are the tables dropped; their indexes, constraints (the foreign keys to
    /// <c>"ServiceOrders"</c>) and policies go with them. The old migrations created no trigger and no function for these
    /// tables, so nothing else is orphaned.</para>
    /// </summary>
    public partial class DropFieldServiceTablesFromKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $drop$
                DECLARE
                    t text;
                    n bigint;
                    previous_row_security text := current_setting('row_security');
                BEGIN
                    PERFORM set_config('row_security', 'off', true);

                    FOREACH t IN ARRAY ARRAY['field_contractor_us_postings', 'field_icerp_paving_measurements', 'field_manhole_audits', 'ContractorUsLedgerEntries', 'UsCatalogItems']
                    LOOP
                        IF to_regclass(format('public.%I', t)) IS NOT NULL THEN
                            EXECUTE format('SELECT count(*) FROM public.%I', t) INTO n;
                            IF n > 0 THEN
                                RAISE EXCEPTION 'Cannot drop % from the kernel database: it holds % row(s). Move them to the FieldService database first.', t, n;
                            END IF;
                        END IF;
                    END LOOP;

                    PERFORM set_config('row_security', previous_row_security, true);

                    FOREACH t IN ARRAY ARRAY['field_contractor_us_postings', 'field_icerp_paving_measurements', 'field_manhole_audits', 'ContractorUsLedgerEntries', 'UsCatalogItems']
                    LOOP
                        EXECUTE format('DROP TABLE IF EXISTS public.%I', t);
                    END LOOP;
                END
                $drop$;
                """);
        }

        /// <summary>
        /// Recreates the five tables, empty, exactly as this chain's old migrations left them — columns, primary keys, the
        /// foreign keys to <c>"ServiceOrders"</c> (Restrict), the indexes, and the <c>tenant_isolation_policy</c> of the four
        /// that had one (<c>field_contractor_us_postings</c> never had a policy in this chain). There is no data to restore:
        /// Up only ever dropped empty tables. Idempotent — a table, index or policy that exists is left as it is.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS public."ContractorUsLedgerEntries" (
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

                CREATE TABLE IF NOT EXISTS public."UsCatalogItems" (
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

                CREATE TABLE IF NOT EXISTS public.field_contractor_us_postings (
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

                CREATE TABLE IF NOT EXISTS public.field_icerp_paving_measurements (
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

                CREATE TABLE IF NOT EXISTS public.field_manhole_audits (
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
                    ON public."ContractorUsLedgerEntries" ("OrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_ContractorUsLedgerEntries_ServiceOrderId"
                    ON public."ContractorUsLedgerEntries" ("ServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_UsCatalogItems_OrganizationId_ItemCode"
                    ON public."UsCatalogItems" ("OrganizationId", "ItemCode");
                CREATE INDEX IF NOT EXISTS "IX_field_contractor_us_postings_ContractorOrganizationId_Poste~"
                    ON public.field_contractor_us_postings ("ContractorOrganizationId", "PostedAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_field_contractor_us_postings_ServiceOrderId"
                    ON public.field_contractor_us_postings ("ServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_field_icerp_paving_measurements_ChildServiceOrderId"
                    ON public.field_icerp_paving_measurements ("ChildServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_field_icerp_paving_measurements_OrganizationId"
                    ON public.field_icerp_paving_measurements ("OrganizationId");
                CREATE INDEX IF NOT EXISTS "IX_field_icerp_paving_measurements_ParentServiceOrderId"
                    ON public.field_icerp_paving_measurements ("ParentServiceOrderId");
                CREATE INDEX IF NOT EXISTS "IX_field_manhole_audits_ConduitFeatureId"
                    ON public.field_manhole_audits ("ConduitFeatureId");
                CREATE INDEX IF NOT EXISTS "IX_field_manhole_audits_OrganizationId"
                    ON public.field_manhole_audits ("OrganizationId");

                ALTER TABLE public."ContractorUsLedgerEntries" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public."UsCatalogItems" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.field_icerp_paving_measurements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.field_manhole_audits ENABLE ROW LEVEL SECURITY;

                DO $policies$
                DECLARE
                    t text;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['ContractorUsLedgerEntries', 'UsCatalogItems', 'field_icerp_paving_measurements', 'field_manhole_audits']
                    LOOP
                        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'public'
                                       AND tablename = t AND policyname = 'tenant_isolation_policy') THEN
                            EXECUTE format(
                                $policy$CREATE POLICY tenant_isolation_policy ON public.%I
                                    AS PERMISSIVE
                                    FOR ALL
                                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)$policy$,
                                t);
                        END IF;
                    END LOOP;
                END
                $policies$;
                """);
        }
    }
}
