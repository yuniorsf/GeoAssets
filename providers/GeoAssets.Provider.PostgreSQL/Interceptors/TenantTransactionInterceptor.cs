using System.Data.Common;
using GeoAssets.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GeoAssets.Provider.PostgreSQL.Interceptors;

/// <summary>
/// Companion to <see cref="TenantSessionInterceptor"/>: re-stamps the current tenant's
/// organization id as a session-local setting on every transaction start, not just on
/// connection open. Under PgBouncer <c>pool_mode = transaction</c>, a physical connection is
/// multiplexed across many client sessions and <c>ConnectionOpenedAsync</c> only fires once
/// per physical connection — not once per transaction — so a setting applied there can
/// silently keep applying to a later transaction belonging to a different tenant once the
/// connection is returned to the pool and handed to another client. Setting it again with
/// <c>is_local = true</c> on <c>TransactionStartedAsync</c> scopes the setting to the
/// transaction itself: Postgres automatically discards it at <c>COMMIT</c>/<c>ROLLBACK</c>,
/// so it can never leak into the next transaction multiplexed over the same connection.
/// </summary>
public sealed class TenantTransactionInterceptor(ITenantContext tenantContext) : DbTransactionInterceptor
{
    private const string SetOrganizationIdSql =
        "SELECT set_config('app.current_organization_id', @orgId, true);";

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection npgsqlConnection)
        {
            using var command = npgsqlConnection.CreateCommand();
            command.CommandText = SetOrganizationIdSql;
            command.Transaction = result as NpgsqlTransaction;
            command.Parameters.Add(new NpgsqlParameter("orgId", tenantContext.CurrentOrganizationId.ToString()));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return await base.TransactionStartedAsync(connection, eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }
}
