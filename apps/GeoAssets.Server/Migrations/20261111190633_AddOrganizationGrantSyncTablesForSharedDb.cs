using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationGrantSyncTablesForSharedDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationGrantMirror_GranteeOrganizationId_ResourceOrgan~",
                table: "OrganizationGrantMirror",
                columns: new[] { "GranteeOrganizationId", "ResourceOrganizationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationGrantMirror");

            migrationBuilder.DropTable(
                name: "OrganizationGrantSyncCursor");
        }
    }
}
