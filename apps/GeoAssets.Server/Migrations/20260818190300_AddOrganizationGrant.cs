using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AllowedActions = table.Column<string>(type: "text", nullable: false),
                    RequiredRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationGrants_GranteeOrganizationId_ResourceOrganizati~",
                table: "OrganizationGrants",
                columns: new[] { "GranteeOrganizationId", "ResourceOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationGrants_IsActive",
                table: "OrganizationGrants",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationGrants");
        }
    }
}
