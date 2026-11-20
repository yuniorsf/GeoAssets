using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoAssets.IpBilling.Server.Migrations
{
    /// <inheritdoc />
    public partial class IpBillingMunicipioGrantSyncOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MunicipioGrantSyncRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MunicipioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IbgeCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    DistribuidoraOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrefeituraOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousPrefeituraOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousIbgeCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    PreviousDistribuidoraOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeasedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MunicipioGrantSyncRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MunicipioGrantSyncRequests_MunicipioId_Id",
                table: "MunicipioGrantSyncRequests",
                columns: new[] { "MunicipioId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MunicipioGrantSyncRequests_NextAttemptAt",
                table: "MunicipioGrantSyncRequests",
                column: "NextAttemptAt",
                filter: "\"CompletedAt\" IS NULL AND \"FailedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MunicipioGrantSyncRequests");
        }
    }
}
