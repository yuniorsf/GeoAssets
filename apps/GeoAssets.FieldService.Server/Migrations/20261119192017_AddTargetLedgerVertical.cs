using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <summary>
    /// XD01-379 — the global target ledger: "ServiceOrderTargets" is keyed by ("VerticalId", "ServiceOrderId"). Every
    /// existing row is an in-process order's: "VerticalId" is backfilled to 'kernel' by its default and the new
    /// "KernelOrderId" to the row's "ServiceOrderId". The foreign key to "ServiceOrders" (ON DELETE CASCADE) moves to
    /// "KernelOrderId", which a remote vertical's target leaves null — its order has no row there; a trigger derives it
    /// (<see cref="KernelOrderTrigger"/>) and a check constraint states the rule. The unique (order, feature) index becomes (vertical, order, feature). Row-level
    /// security: see <see cref="UpPolicies"/>. Down refuses to run while remote verticals' targets exist, rather than
    /// silently losing them.
    /// </summary>
    public partial class AddTargetLedgerVertical : Migration
    {
        /// <summary>
        /// Row-level security for the ledger rows of remote verticals — the same two policies as the GeoAssets.Server
        /// history's migration of the same name, since they name no grant table. A remote vertical's order has no
        /// "ServiceOrders" row, so the policies that follow an order (tenant_isolation_policy's grant clause, spelled
        /// out against "OrganizationGrantMirror" in this history, and its WITH CHECK, and the dispatch-recipient
        /// policies) can never admit one; such a row is scoped by its own "OrganizationId" instead.
        ///
        ///   ledger_vertical_isolation_policy — RESTRICTIVE, FOR ALL: a row of another vertical than 'kernel' is visible
        ///   and writable only by its own organization's session. True for every 'kernel' row, so in-process targets
        ///   behave exactly as before; it also stops an order-following policy from reaching a remote order whose id
        ///   happens to equal an in-process order's.
        ///   ledger_vertical_tenant_policy — PERMISSIVE, FOR ALL: the organization's session reads and writes its own
        ///   remote-vertical rows (tenant_isolation_policy's WITH CHECK requires a parent order, which they lack).
        ///
        /// Existing policies are left as they are. No FORCE and no GRANT, as everywhere else in this history.
        /// </summary>
        private const string UpPolicies =
            """
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
            """;

        /// <summary>
        /// Refuses the revert while it would lose data: the previous schema cannot hold a remote vertical's target (no
        /// vertical column, and a foreign key on "ServiceOrderId" every such row violates). In-process targets always
        /// reference an existing order (the foreign key on "KernelOrderId"), so restoring the old key loses nothing.
        /// </summary>
        private const string DownGuard =
            """
            DO $$
            DECLARE
                remote_targets bigint;
            BEGIN
                SELECT count(*) INTO remote_targets FROM "ServiceOrderTargets" WHERE "VerticalId" <> 'kernel';
                IF remote_targets > 0 THEN
                    RAISE EXCEPTION 'Cannot revert AddTargetLedgerVertical: "ServiceOrderTargets" holds % target(s) of remote verticals that the previous schema cannot store. Archive and delete them first.', remote_targets;
                END IF;
            END $$;
            """;

        /// <summary>
        /// "KernelOrderId" is derived, never chosen: a BEFORE trigger sets it from "VerticalId" and "ServiceOrderId" on
        /// every insert and on every update of those columns, so a writer that predates the column — or that names it
        /// wrongly — still files an in-process target under its order (and its foreign key), and a remote vertical's
        /// target under none. The application writes the same value itself (SQLite has no trigger), and the check
        /// constraint states the rule for both engines.
        /// </summary>
        private const string KernelOrderTrigger =
            """
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
            """;

        private const string DownPolicies =
            """
            DROP POLICY IF EXISTS ledger_vertical_tenant_policy ON "ServiceOrderTargets";
            DROP POLICY IF EXISTS ledger_vertical_isolation_policy ON "ServiceOrderTargets";
            DROP TRIGGER IF EXISTS service_order_targets_kernel_order_id ON "ServiceOrderTargets";
            DROP FUNCTION IF EXISTS service_order_targets_set_kernel_order_id();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceOrderTargets_ServiceOrders_ServiceOrderId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderTargets_ServiceOrderId_FeatureId",
                table: "ServiceOrderTargets");

            migrationBuilder.AddColumn<int>(
                name: "Exclusivity",
                table: "ServiceOrderTargets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KernelOrderId",
                table: "ServiceOrderTargets",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderTypeId",
                table: "ServiceOrderTargets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerticalId",
                table: "ServiceOrderTargets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "kernel");

            // Every row so far is an in-process order's: it references its order through the new column
            // before the check constraint and the foreign key are created.
            migrationBuilder.Sql(
                """
                UPDATE "ServiceOrderTargets" SET "VerticalId" = 'kernel', "KernelOrderId" = "ServiceOrderId";
                """);

            migrationBuilder.Sql(KernelOrderTrigger);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_KernelOrderId",
                table: "ServiceOrderTargets",
                column: "KernelOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_VerticalId_ServiceOrderId_FeatureId",
                table: "ServiceOrderTargets",
                columns: new[] { "VerticalId", "ServiceOrderId", "FeatureId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceOrderTargets_KernelOrderId",
                table: "ServiceOrderTargets",
                sql: "CASE WHEN \"VerticalId\" = 'kernel' THEN \"KernelOrderId\" IS NOT NULL AND \"KernelOrderId\" = \"ServiceOrderId\" ELSE \"KernelOrderId\" IS NULL END");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceOrderTargets_ServiceOrders_KernelOrderId",
                table: "ServiceOrderTargets",
                column: "KernelOrderId",
                principalTable: "ServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(UpPolicies);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownGuard);
            migrationBuilder.Sql(DownPolicies);

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceOrderTargets_ServiceOrders_KernelOrderId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderTargets_KernelOrderId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderTargets_VerticalId_ServiceOrderId_FeatureId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceOrderTargets_KernelOrderId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropColumn(
                name: "Exclusivity",
                table: "ServiceOrderTargets");

            migrationBuilder.DropColumn(
                name: "KernelOrderId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropColumn(
                name: "OrderTypeId",
                table: "ServiceOrderTargets");

            migrationBuilder.DropColumn(
                name: "VerticalId",
                table: "ServiceOrderTargets");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderTargets_ServiceOrderId_FeatureId",
                table: "ServiceOrderTargets",
                columns: new[] { "ServiceOrderId", "FeatureId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceOrderTargets_ServiceOrders_ServiceOrderId",
                table: "ServiceOrderTargets",
                column: "ServiceOrderId",
                principalTable: "ServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
