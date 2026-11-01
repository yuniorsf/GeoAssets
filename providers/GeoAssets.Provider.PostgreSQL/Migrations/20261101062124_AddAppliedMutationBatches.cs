using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAppliedMutationBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applied_mutation_batches",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AppliedByUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    SourceOrderTypeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AppliedItemsJson = table.Column<string>(type: "text", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applied_mutation_batches", x => x.BatchId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_applied_mutation_batches_AppliedAtUtc",
                table: "applied_mutation_batches",
                column: "AppliedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applied_mutation_batches");
        }
    }
}
