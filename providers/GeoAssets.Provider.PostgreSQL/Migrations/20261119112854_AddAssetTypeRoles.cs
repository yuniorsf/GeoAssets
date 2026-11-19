using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Provider.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTypeRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Semantic asset-type roles (XD01-367, ADR-001 Part 1.1): a NOT NULL text[] defaulting
            // to empty, so every existing row reads as "no roles" until seeded or edited.
            //
            // No Row-Level Security change: asset_type's tenant_isolation_policy
            // (AddAssetTypeRowLevelSecurity) is row-scoped on "OrganizationId" and applies to
            // every column of the row, the new one included; no column-level grant exists on
            // this table either.
            migrationBuilder.AddColumn<List<string>>(
                name: "Roles",
                table: "asset_type",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "Roles",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "Roles",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "Roles",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"),
                column: "Roles",
                value: new List<string> { "occupiable-support", "isp:pole" });

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"),
                column: "Roles",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"),
                column: "Roles",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000007"),
                column: "Roles",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "asset_type",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000008"),
                column: "Roles",
                value: new List<string>());

            // The IP-billing types are seeded by SeedIpBillingAssetTypes' raw InsertData, outside
            // the EF model, so their roles are set here; they mirror
            // GeoAssets.IpBilling.AssetTypes.IpBillingAssetTypes. A deleted row updates nothing.
            migrationBuilder.Sql(
                """
                UPDATE asset_type SET "Roles" = ARRAY['ip-billing:transformer'] WHERE "Id" = '00000000-0000-0000-0000-000000000009';
                UPDATE asset_type SET "Roles" = ARRAY['ip-billing:pole'] WHERE "Id" = '00000000-0000-0000-0000-00000000000a';
                UPDATE asset_type SET "Roles" = ARRAY['ip-billing:lamp-group-24h'] WHERE "Id" = '00000000-0000-0000-0000-00000000000b';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_asset_type_Roles",
                table: "asset_type",
                column: "Roles")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the column would silently discard roles someone gave a type. Refuse while
            // any type other than the ones this migration seeded carries a role.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM asset_type
                        WHERE cardinality("Roles") > 0
                          AND "Id" NOT IN (
                              '00000000-0000-0000-0000-000000000004',
                              '00000000-0000-0000-0000-000000000009',
                              '00000000-0000-0000-0000-00000000000a',
                              '00000000-0000-0000-0000-00000000000b')) THEN
                        RAISE EXCEPTION 'AddAssetTypeRoles cannot be reverted: asset types other than the seeded ones carry roles that dropping "Roles" would discard.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_asset_type_Roles",
                table: "asset_type");

            migrationBuilder.DropColumn(
                name: "Roles",
                table: "asset_type");
        }
    }
}
