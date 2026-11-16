using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialFieldServiceIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractorUsLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    UsCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PhotoProofJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorUsLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "field_manhole_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConduitFeatureId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoreOccupancyPercentage = table.Column<double>(type: "double precision", nullable: false),
                    SubDuctsJson = table.Column<string>(type: "text", nullable: false),
                    SpliceEnclosureCondition = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Nr33ChecklistAttributesJson = table.Column<string>(type: "text", nullable: false),
                    AuditedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_manhole_audits", x => x.Id);
                });

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
                name: "OrganizationGrantMirror",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationGrantMirror", x => x.GrantId);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationGrantSyncCursor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationGrantSyncCursor", x => x.Id);
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
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
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
                name: "UsCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaseUsFactor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RegionalMultiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 1m),
                    MonetaryConversionRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInboxProcessedMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInboxProcessedMessages", x => new { x.MessageId, x.ConsumerName });
                });

            migrationBuilder.CreateTable(
                name: "WorkflowOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    PartitionKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    CatalogInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CausationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    HeadersJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowOutboxMessages", x => x.Id);
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
                name: "field_contractor_us_postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ContractorOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitUs = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalUs = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    MonetaryRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalBrl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_contractor_us_postings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_field_contractor_us_postings_ServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "field_icerp_paving_measurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ChildServiceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LengthMeters = table.Column<double>(type: "double precision", nullable: false),
                    WidthMeters = table.Column<double>(type: "double precision", nullable: false),
                    DepthMeters = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAreaSquareMeters = table.Column<double>(type: "double precision", nullable: false),
                    ActualAreaSquareMeters = table.Column<double>(type: "double precision", nullable: false),
                    VolumeCubicMeters = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_icerp_paving_measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_field_icerp_paving_measurements_ServiceOrders_Child",
                        column: x => x.ChildServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_field_icerp_paving_measurements_ServiceOrders_Parent",
                        column: x => x.ParentServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "IX_ContractorUsLedgerEntries_OrganizationId",
                table: "ContractorUsLedgerEntries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorUsLedgerEntries_ServiceOrderId",
                table: "ContractorUsLedgerEntries",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_field_contractor_us_postings_ContractorOrganizationId_Poste~",
                table: "field_contractor_us_postings",
                columns: new[] { "ContractorOrganizationId", "PostedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_field_contractor_us_postings_ServiceOrderId",
                table: "field_contractor_us_postings",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_field_icerp_paving_measurements_ChildServiceOrderId",
                table: "field_icerp_paving_measurements",
                column: "ChildServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_field_icerp_paving_measurements_OrganizationId",
                table: "field_icerp_paving_measurements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_field_icerp_paving_measurements_ParentServiceOrderId",
                table: "field_icerp_paving_measurements",
                column: "ParentServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_field_manhole_audits_ConduitFeatureId",
                table: "field_manhole_audits",
                column: "ConduitFeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_field_manhole_audits_OrganizationId",
                table: "field_manhole_audits",
                column: "OrganizationId");

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
                name: "IX_OrganizationGrantMirror_GranteeOrganizationId_ResourceOrgan~",
                table: "OrganizationGrantMirror",
                columns: new[] { "GranteeOrganizationId", "ResourceOrganizationId" });

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
                name: "IX_ServiceOrders_OrganizationId",
                table: "ServiceOrders",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_ParentOrderId",
                table: "ServiceOrders",
                column: "ParentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_Status",
                table: "ServiceOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UsCatalogItems_OrganizationId_ItemCode",
                table: "UsCatalogItems",
                columns: new[] { "OrganizationId", "ItemCode" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInboxProcessedMessages_ExpiresAtUtc",
                table: "WorkflowInboxProcessedMessages",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowOutboxMessages_Status_LockedUntilUtc",
                table: "WorkflowOutboxMessages",
                columns: new[] { "Status", "LockedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowOutboxMessages_Status_NextRetryAtUtc_CreatedAtUtc",
                table: "WorkflowOutboxMessages",
                columns: new[] { "Status", "NextRetryAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractorUsLedgerEntries");

            migrationBuilder.DropTable(
                name: "field_contractor_us_postings");

            migrationBuilder.DropTable(
                name: "field_icerp_paving_measurements");

            migrationBuilder.DropTable(
                name: "field_manhole_audits");

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
                name: "OrganizationGrantMirror");

            migrationBuilder.DropTable(
                name: "OrganizationGrantSyncCursor");

            migrationBuilder.DropTable(
                name: "UsCatalogItems");

            migrationBuilder.DropTable(
                name: "WorkflowInboxProcessedMessages");

            migrationBuilder.DropTable(
                name: "WorkflowOutboxMessages");

            migrationBuilder.DropTable(
                name: "ServiceOrders");

            migrationBuilder.DropTable(
                name: "OrderTypes");
        }
    }
}
