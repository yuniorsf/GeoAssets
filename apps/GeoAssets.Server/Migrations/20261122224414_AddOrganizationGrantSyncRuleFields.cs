using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoAssets.Server.Migrations.GeoIdentityDb
{
    /// <summary>
    /// XD01-328: the grant sync feed carries the rest of what the cross-organization rule (XD01-326) decides by —
    /// <c>"AllowedActions"</c>, <c>"RequiredRole"</c>, <c>"RequiresScopedRole"</c>, <c>"OrderTypeIds"</c> on
    /// <c>"OrganizationGrantSyncOutbox"</c> — and the scoped role assignment feed gets its outbox,
    /// <c>"ScopedRoleAssignmentSyncOutbox"</c> (one row per (user, organization) whose roles there may have changed).
    ///
    /// The rows recorded before carry none of the new fields (they migrate as <c>'[]'</c>, null and <c>false</c>), and a
    /// consumer has already applied most of them. So Up re-records every grant's current state, in grant order: the next
    /// poll of every consumer brings each grant up to date, its rule fields included. It records no scoped role
    /// assignment; every one made from here on is. Down drops the columns and the table; the re-recorded rows stay, as
    /// ordinary snapshots.
    /// </summary>
    public partial class AddOrganizationGrantSyncRuleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedActions",
                table: "OrganizationGrantSyncOutbox",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "OrderTypeIds",
                table: "OrganizationGrantSyncOutbox",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "RequiredRole",
                table: "OrganizationGrantSyncOutbox",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresScopedRole",
                table: "OrganizationGrantSyncOutbox",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ScopedRoleAssignmentSyncOutbox",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopedRoleAssignmentSyncOutbox", x => x.SequenceNumber);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "OrganizationGrantSyncOutbox"
                    ("GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "IsActive", "ExpiresAt",
                     "AllowedActions", "RequiredRole", "RequiresScopedRole", "OrderTypeIds", "RecordedAtUtc")
                SELECT og."Id", og."GranteeOrganizationId", og."ResourceOrganizationId", og."ResourceType", og."ScopeId", og."IsActive", og."ExpiresAt",
                       og."AllowedActions", og."RequiredRole", og."RequiresScopedRole", og."OrderTypeIds", now()
                FROM "OrganizationGrants" og
                ORDER BY og."GrantedAt", og."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScopedRoleAssignmentSyncOutbox");

            migrationBuilder.DropColumn(
                name: "AllowedActions",
                table: "OrganizationGrantSyncOutbox");

            migrationBuilder.DropColumn(
                name: "OrderTypeIds",
                table: "OrganizationGrantSyncOutbox");

            migrationBuilder.DropColumn(
                name: "RequiredRole",
                table: "OrganizationGrantSyncOutbox");

            migrationBuilder.DropColumn(
                name: "RequiresScopedRole",
                table: "OrganizationGrantSyncOutbox");
        }
    }
}
