using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.FieldService.Server.Migrations
{
    /// <summary>
    /// XD01-328: the two mirrors a host without the identity database decides cross-organization service-order access by.
    /// <c>"OrganizationGrantMirror"</c> gains the rest of the grant the rule (XD01-326) reads — <c>"AllowedActionsJson"</c>,
    /// <c>"RequiredRole"</c>, <c>"RequiresScopedRole"</c>, <c>"OrderTypeIdsJson"</c> (existing rows migrate as <c>'[]'</c>,
    /// null, <c>false</c>, <c>'[]'</c> until the grant sync worker rewrites them from the feed's re-recorded snapshots) — and
    /// <c>"ScopedRoleAssignmentMirror"</c>/<c>"ScopedRoleAssignmentSyncCursor"</c> mirror the roles users hold on other
    /// organizations. No row-level security policy reads them: like the grant mirror they hold no organization's data of
    /// their own, and the policies are unchanged. Down drops them.
    /// </summary>
    public partial class AddCrossOrganizationSyncMirrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedActionsJson",
                table: "OrganizationGrantMirror",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "OrderTypeIdsJson",
                table: "OrganizationGrantMirror",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "RequiredRole",
                table: "OrganizationGrantMirror",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresScopedRole",
                table: "OrganizationGrantMirror",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ScopedRoleAssignmentMirror",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalObjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RoleNamesJson = table.Column<string>(type: "text", nullable: false),
                    PermissionCodesJson = table.Column<string>(type: "text", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopedRoleAssignmentMirror", x => new { x.UserId, x.ResourceOrganizationId });
                });

            migrationBuilder.CreateTable(
                name: "ScopedRoleAssignmentSyncCursor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopedRoleAssignmentSyncCursor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignmentMirror_ExternalObjectId",
                table: "ScopedRoleAssignmentMirror",
                column: "ExternalObjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScopedRoleAssignmentMirror");

            migrationBuilder.DropTable(
                name: "ScopedRoleAssignmentSyncCursor");

            migrationBuilder.DropColumn(
                name: "AllowedActionsJson",
                table: "OrganizationGrantMirror");

            migrationBuilder.DropColumn(
                name: "OrderTypeIdsJson",
                table: "OrganizationGrantMirror");

            migrationBuilder.DropColumn(
                name: "RequiredRole",
                table: "OrganizationGrantMirror");

            migrationBuilder.DropColumn(
                name: "RequiresScopedRole",
                table: "OrganizationGrantMirror");
        }
    }
}
