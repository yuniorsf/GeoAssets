using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowOutboxAndInboxTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "WorkflowInboxProcessedMessages");

            migrationBuilder.DropTable(
                name: "WorkflowOutboxMessages");
        }
    }
}
