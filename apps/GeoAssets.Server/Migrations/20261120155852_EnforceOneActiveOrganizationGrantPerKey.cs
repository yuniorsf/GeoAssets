using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoAssets.Server.Migrations
{
    /// <summary>
    /// XD01-394: one active organization grant per key. The unique index <c>UX_OrganizationGrants_ActiveKey</c> covers
    /// <c>("GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId")</c> over the rows flagged
    /// <c>"IsActive"</c> (expired or not), <c>NULLS NOT DISTINCT</c> so a null resource type or scope is one key value —
    /// the key the kernel's generic grant upsert (XD01-385) matches by, which until now only the application enforced, so
    /// two concurrent upserts of one key could both insert.
    ///
    /// <para><b>Guarded</b>, before the index is created: if any key already has more than one active grant, the migration
    /// raises — <c>Cannot enforce one active organization grant per key: N key(s) have several active grants: …</c>, naming
    /// each key and its grant ids, oldest first — and changes nothing (the migration's transaction rolls back). It never
    /// deactivates or deletes a grant itself: which grant should survive is an operator's decision; the clean-up query
    /// (keep the oldest, deactivate the rest) is documented under "Duplicate active organization grants" in
    /// <c>deploy/server/README.md</c>.</para>
    /// </summary>
    public partial class EnforceOneActiveOrganizationGrantPerKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $guard$
                DECLARE
                    duplicate_keys bigint;
                    duplicates text;
                BEGIN
                    SELECT count(*),
                           string_agg(
                               format('(grantee %s, resource %s, resourceType %s, scopeId %s): grants %s',
                                      d."GranteeOrganizationId", d."ResourceOrganizationId",
                                      coalesce(quote_literal(d."ResourceType"), 'null'), coalesce(quote_literal(d."ScopeId"), 'null'),
                                      d.grant_ids),
                               '; ' ORDER BY d."GranteeOrganizationId", d."ResourceOrganizationId", d."ResourceType", d."ScopeId")
                      INTO duplicate_keys, duplicates
                      FROM (SELECT "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId",
                                   string_agg("Id"::text, ', ' ORDER BY "GrantedAt", "Id") AS grant_ids
                              FROM "OrganizationGrants"
                             WHERE "IsActive"
                             GROUP BY "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId"
                            HAVING count(*) > 1) AS d;

                    IF duplicate_keys > 0 THEN
                        RAISE EXCEPTION 'Cannot enforce one active organization grant per key: % key(s) have several active grants: %. Deactivate all but one grant per key first (see "Duplicate active organization grants" in deploy/server/README.md).', duplicate_keys, duplicates;
                    END IF;
                END
                $guard$;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_OrganizationGrants_ActiveKey",
                table: "OrganizationGrants",
                columns: new[] { "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId" },
                unique: true,
                filter: "\"IsActive\"")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OrganizationGrants_ActiveKey",
                table: "OrganizationGrants");
        }
    }
}
