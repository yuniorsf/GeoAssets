using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations.GeoIdentityDb
{
    /// <summary>
    /// XD01-411: <c>"OrganizationGrants"."Source"</c>, who owns a grant — <c>'service'</c> (a vertical host, through the
    /// kernel's <c>organization-grants</c> routes) or <c>'administrator'</c> (issued by the resource organization's
    /// administrators). Every existing grant was provisioned by a vertical host, so it migrates as <c>'service'</c> and stays
    /// read-only to administrators. Down drops the column.
    /// </summary>
    public partial class AddOrganizationGrantSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "OrganizationGrants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "service");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "OrganizationGrants");
        }
    }
}
