using System.Data.Common;
using GeoAssets.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GeoAssets.Provider.PostgreSQL.Interceptors;

/// <summary>
/// Stamps every opened PostgreSQL connection with the current tenant's organization id as a
/// session-local setting (<c>app.current_organization_id</c>), which <c>geo_entity</c>'s native
/// Row-Level Security policy (see the <c>AddGeoEntityRowLevelSecurity</c> migration) reads via
/// <c>current_setting</c>. This is defense-in-depth underneath <see cref="GeoAssets.Provider.PostgreSQL.Data.GeoAssetsDbContext"/>'s
/// EF Core global query filter: even a raw SQL query or a bug that bypasses the filter still
/// can't cross tenant boundaries once the database connects as a non-owner role (RLS is a no-op
/// for the table owner unless/until a follow-up also issues <c>FORCE ROW LEVEL SECURITY</c>).
///
/// Always sets the literal organization id — including <see cref="Guid.Empty"/> — rather than
/// clearing the setting to blank/NULL for "no tenant". The RLS policy compares the column to
/// this setting with <c>=</c>, and SQL's <c>NULL = x</c> is never true, so a blank setting would
/// hide every row (including today's single-tenant data, which defaults to
/// <see cref="Guid.Empty"/>) instead of just the rows that belong to a real empty-sentinel
/// "no organization" bucket.
/// </summary>
public sealed class TenantSessionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    private const string SetOrganizationIdSql =
        "SELECT set_config('app.current_organization_id', @orgId, false);";

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection npgsqlConnection)
        {
            using var command = npgsqlConnection.CreateCommand();
            command.CommandText = SetOrganizationIdSql;
            command.Parameters.Add(new NpgsqlParameter("orgId", tenantContext.CurrentOrganizationId.ToString()));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
