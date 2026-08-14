using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    InitialStateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOrders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ParentOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    AttributesJson = table.Column<string>(type: "text", nullable: false),
                    FeatureIdsJson = table.Column<string>(type: "text", nullable: false),
                    SelectionSpecJson = table.Column<string>(type: "text", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceOrders_ServiceOrders_ParentOrderId",
                        column: x => x.ParentOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderActionPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FromStateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderActionPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderActionPermissions_OrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "OrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderCreationPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCreationPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCreationPolicies_OrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "OrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderTypeStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTypeStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderTypeStates_OrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "OrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderTypeTransitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FromStateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToStateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TriggerAction = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTypeTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderTypeTransitions_OrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "OrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderActionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    PerformedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ResultingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    AgentInvocationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderActionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderActionLogs_ServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderDispatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    DispatchedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    AgentInvocationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderDispatches_ServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderActionLogs_PerformedAt",
                table: "OrderActionLogs",
                column: "PerformedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderActionLogs_PerformedBy",
                table: "OrderActionLogs",
                column: "PerformedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OrderActionLogs_ServiceOrderId",
                table: "OrderActionLogs",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderActionPermissions_OrderTypeId_Action",
                table: "OrderActionPermissions",
                columns: new[] { "OrderTypeId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderCreationPolicies_OrderTypeId",
                table: "OrderCreationPolicies",
                column: "OrderTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDispatches_ServiceOrderId",
                table: "OrderDispatches",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDispatches_TargetId_TargetType",
                table: "OrderDispatches",
                columns: new[] { "TargetId", "TargetType" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderTypeStates_OrderTypeId_Key",
                table: "OrderTypeStates",
                columns: new[] { "OrderTypeId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderTypeTransitions_OrderTypeId_FromStateKey",
                table: "OrderTypeTransitions",
                columns: new[] { "OrderTypeId", "FromStateKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_AssignedTo",
                table: "ServiceOrders",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_CreatedAt",
                table: "ServiceOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_CreatedBy",
                table: "ServiceOrders",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_OrderTypeId",
                table: "ServiceOrders",
                column: "OrderTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_ParentOrderId",
                table: "ServiceOrders",
                column: "ParentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_Status",
                table: "ServiceOrders",
                column: "Status");

            // Postgres has no native auto-updating rowversion type (unlike SQL Server), so
            // ServiceOrderRecordConfiguration's provider-agnostic `RowVersion.IsRowVersion()`
            // needs a trigger to actually change the value on every UPDATE — otherwise EF's
            // optimistic-concurrency check (WHERE "RowVersion" = @original) would always match
            // and concurrent-writer conflicts would silently go undetected. Mirrors the same
            // problem GeoAssets.Workflow.EFCore.Tests' SqliteTestDbContext already solves for
            // SQLite via an AFTER UPDATE trigger. gen_random_uuid() is built into Postgres 13+
            // core (no extension required).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION touch_service_orders_row_version() RETURNS trigger AS $$
                BEGIN
                    NEW."RowVersion" := uuid_send(gen_random_uuid());
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER service_orders_row_version_touch
                BEFORE UPDATE ON "ServiceOrders"
                FOR EACH ROW EXECUTE FUNCTION touch_service_orders_row_version();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS service_orders_row_version_touch ON "ServiceOrders";
                DROP FUNCTION IF EXISTS touch_service_orders_row_version();
                """);

            migrationBuilder.DropTable(
                name: "OrderActionLogs");

            migrationBuilder.DropTable(
                name: "OrderActionPermissions");

            migrationBuilder.DropTable(
                name: "OrderCreationPolicies");

            migrationBuilder.DropTable(
                name: "OrderDispatches");

            migrationBuilder.DropTable(
                name: "OrderTypeStates");

            migrationBuilder.DropTable(
                name: "OrderTypeTransitions");

            migrationBuilder.DropTable(
                name: "ServiceOrders");

            migrationBuilder.DropTable(
                name: "OrderTypes");
        }
    }
}
