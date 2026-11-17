using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <summary>
    /// XD01-325: a role assignment gains an optional organization scope. The composite primary key
    /// <c>(UserId, RoleId)</c> gives way to a surrogate <c>Id</c>, because the same user may now
    /// hold the same role once per scope and a primary key cannot include the nullable
    /// <c>OrganizationId</c>. Every existing row keeps <c>OrganizationId = NULL</c>, which is what
    /// "assigned in the user's home organization" (the only meaning until now) is stored as.
    /// </summary>
    public partial class AddUserRoleOrganizationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles");

            // Existing rows each need their own key value, so the column is added with a
            // per-row generated default; a constant default would give every row the same Id
            // and the primary key below could not be created. The default is then dropped: new
            // rows get their Id from the application, as every other Guid key in this model does.
            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "UserRoles",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.Sql("""ALTER TABLE "UserRoles" ALTER COLUMN "Id" DROP DEFAULT;""");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "UserRoles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_OrganizationId",
                table: "UserRoles",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true,
                filter: "\"OrganizationId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId_OrganizationId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId", "OrganizationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Organizations_OrganizationId",
                table: "UserRoles",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema has no notion of scope: a surviving organization-scoped row
            // would be read as a home assignment (a privilege the user was never given at home),
            // and could also collide with the user's real home assignment under the restored
            // (UserId, RoleId) key. Scoped assignments are therefore removed on the way down.
            migrationBuilder.Sql("""DELETE FROM "UserRoles" WHERE "OrganizationId" IS NOT NULL;""");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Organizations_OrganizationId",
                table: "UserRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_OrganizationId",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_UserId_RoleId_OrganizationId",
                table: "UserRoles");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "UserRoles");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "UserRoles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" });
        }
    }
}
