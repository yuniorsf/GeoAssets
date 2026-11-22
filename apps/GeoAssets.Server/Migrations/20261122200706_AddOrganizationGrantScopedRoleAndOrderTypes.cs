using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <summary>
    /// XD01-326: two opt-in columns on <c>"OrganizationGrants"</c>. <c>"RequiresScopedRole"</c> — a grant only roles held on
    /// the resource organization may use — and <c>"OrderTypeIds"</c> — the service-order types a grant is narrowed to, as JSON
    /// text like <c>"AllowedActions"</c>. Every existing row migrates as <c>false</c> and <c>'[]'</c> (every order type): the
    /// organization-wide semantics it was created with, unchanged. Down drops both columns; a grant created with either since
    /// would then fall back to those semantics, so revoke such grants before rolling back.
    /// </summary>
    public partial class AddOrganizationGrantScopedRoleAndOrderTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderTypeIds",
                table: "OrganizationGrants",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresScopedRole",
                table: "OrganizationGrants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderTypeIds",
                table: "OrganizationGrants");

            migrationBuilder.DropColumn(
                name: "RequiresScopedRole",
                table: "OrganizationGrants");
        }
    }
}
