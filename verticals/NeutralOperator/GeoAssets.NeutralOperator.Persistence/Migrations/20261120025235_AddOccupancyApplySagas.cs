using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.NeutralOperator.Persistence.Migrations
{
    /// <summary>
    /// The approval saga (XD01-383, ADR-001 Phase 7): <c>occupancy_apply_sagas</c> (one row per approval waiting for — or
    /// answered by — the kernel's apply of its asset mutation batch; ids, states and instants, no occupancy entry, so no
    /// row-level-security policy, like the outbox), <c>occupancy_apply_saga_poles</c> (each written pole's entries before the
    /// approval, for the compensation — owner only, with the same <c>tenant_isolation_policy</c> as the occupancy tables), and
    /// the change sets' <c>ApplyBatchId</c>. Additive: no existing row changes.
    /// </summary>
    public partial class AddOccupancyApplySagas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplyBatchId",
                schema: "neutral_operator",
                table: "ServiceOrderOccupancyChangeSets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "occupancy_apply_sagas",
                schema: "neutral_operator",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    TimedOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConflictingFeatureIdsJson = table.Column<string>(type: "text", nullable: false),
                    BatchJson = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_occupancy_apply_sagas", x => x.BatchId);
                });

            migrationBuilder.CreateTable(
                name: "occupancy_apply_saga_poles",
                schema: "neutral_operator",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoleFeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    BaselineVersion = table.Column<int>(type: "integer", nullable: false),
                    AppliedVersion = table.Column<int>(type: "integer", nullable: false),
                    BaselineEntriesJson = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_occupancy_apply_saga_poles", x => new { x.BatchId, x.PoleFeatureId });
                    table.ForeignKey(
                        name: "FK_occupancy_apply_saga_poles_occupancy_apply_sagas_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "neutral_operator",
                        principalTable: "occupancy_apply_sagas",
                        principalColumn: "BatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderOccupancyChangeSets_ApplyBatchId",
                schema: "neutral_operator",
                table: "ServiceOrderOccupancyChangeSets",
                column: "ApplyBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_occupancy_apply_saga_poles_PoleFeatureId_OrganizationId",
                schema: "neutral_operator",
                table: "occupancy_apply_saga_poles",
                columns: new[] { "PoleFeatureId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_occupancy_apply_sagas_OrganizationId",
                schema: "neutral_operator",
                table: "occupancy_apply_sagas",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_occupancy_apply_sagas_ServiceOrderId_Attempt",
                schema: "neutral_operator",
                table: "occupancy_apply_sagas",
                columns: new[] { "ServiceOrderId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_occupancy_apply_sagas_State_LastRequestedAt",
                schema: "neutral_operator",
                table: "occupancy_apply_sagas",
                columns: new[] { "State", "LastRequestedAt" });

            migrationBuilder.Sql(
                """
                ALTER TABLE neutral_operator.occupancy_apply_saga_poles ENABLE ROW LEVEL SECURITY;

                DO $policies$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'neutral_operator'
                                   AND tablename = 'occupancy_apply_saga_poles' AND policyname = 'tenant_isolation_policy') THEN
                        CREATE POLICY tenant_isolation_policy ON neutral_operator.occupancy_apply_saga_poles
                            AS PERMISSIVE
                            FOR ALL
                            USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                            WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                    END IF;
                END
                $policies$;
                """);
        }

        /// <summary>
        /// Guarded: never drops data. Refuses (raises, changing nothing) while a saga is on file or a change set belongs to
        /// one (an <c>ApplyBatchId</c>, or a <c>Pending</c>/<c>Reverted</c> status the earlier code does not know); on an
        /// unused schema it drops the two tables and the column.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $guard$
                DECLARE
                    n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM neutral_operator.occupancy_apply_sagas;
                    IF n > 0 THEN
                        RAISE EXCEPTION 'Cannot drop neutral_operator.occupancy_apply_sagas: it holds % row(s); settle or archive the approval sagas first (nothing was changed).', n;
                    END IF;

                    SELECT count(*) INTO n FROM neutral_operator."ServiceOrderOccupancyChangeSets"
                    WHERE "ApplyBatchId" IS NOT NULL OR "Status" IN ('Pending', 'Reverted');
                    IF n > 0 THEN
                        RAISE EXCEPTION 'Cannot drop neutral_operator."ServiceOrderOccupancyChangeSets"."ApplyBatchId": % change set(s) belong to an approval saga (nothing was changed).', n;
                    END IF;
                END
                $guard$;
                """);

            migrationBuilder.DropTable(
                name: "occupancy_apply_saga_poles",
                schema: "neutral_operator");

            migrationBuilder.DropTable(
                name: "occupancy_apply_sagas",
                schema: "neutral_operator");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderOccupancyChangeSets_ApplyBatchId",
                schema: "neutral_operator",
                table: "ServiceOrderOccupancyChangeSets");

            migrationBuilder.DropColumn(
                name: "ApplyBatchId",
                schema: "neutral_operator",
                table: "ServiceOrderOccupancyChangeSets");
        }
    }
}
