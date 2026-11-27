using System.Collections.Concurrent;
using System.Security.Cryptography;
using GeoAssets.Core.Rendering;
using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The kernel replicas' shared tile versions in the GeoAssets database (ADR-002 D6, XD01-458) — the
/// <see cref="ITileVersionStore"/> of the <c>postgres-spatial</c> module:
/// <list type="bullet">
///   <item><b>Durable</b>: <c>tile_generations</c> (one row per scope ever advanced) and the one-row <c>tile_version_state</c>
///   (epoch, flush count; migration <c>AddTileVersions</c>). The epoch is chosen at random by the first reader
///   (<c>INSERT … ON CONFLICT DO NOTHING</c>), so a database whose tables were created again starts a new one.</item>
///   <item><b>Atomic</b>: <see cref="AdvanceAsync"/> bumps the scopes (in id order, so two writers never lock them in
///   opposite orders) and the flush count with <c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c> and sends
///   <c>pg_notify</c> on <see cref="Channel"/> in the same short transaction: a listener only ever hears committed
///   values, after their commit. Its own transaction, never a writer's — the after-commit invalidation's.</item>
///   <item><b>Announced</b>: <see cref="ListenAsync"/> holds one dedicated, unpooled connection on <c>LISTEN</c>, with a
///   keepalive (<see cref="KeepAliveSeconds"/>) so a dead connection is noticed while waiting; the payload is
///   <see cref="TileVersionNotification"/>'s.</item>
/// </list>
/// Thread-safe; every other statement runs on a pooled connection of its own.
/// </summary>
public sealed class PostgresTileVersionStore : ITileVersionStore
{
    /// <summary>The <c>LISTEN</c>/<c>NOTIFY</c> channel.</summary>
    public const string Channel = "geoassets_tile_versions";

    /// <summary>Seconds of silence after which the listening connection is probed.</summary>
    public const int KeepAliveSeconds = 10;

    private const string CreateStateSql = """INSERT INTO tile_version_state ("Id", "Epoch", "Flushes") VALUES (1, @epoch, 0) ON CONFLICT ("Id") DO NOTHING""";

    private const string LoadStateSql = """SELECT "Epoch", "Flushes" FROM tile_version_state WHERE "Id" = 1""";

    private const string LoadGenerationsSql = """SELECT "Scope", "Generation" FROM tile_generations WHERE "Scope" = ANY(@scopes)""";

    private const string AdvanceSql = """
        INSERT INTO tile_generations ("Scope", "Generation")
        SELECT scope, 1 FROM unnest(@scopes) AS scope
        ON CONFLICT ("Scope") DO UPDATE SET "Generation" = tile_generations."Generation" + 1
        RETURNING "Scope", "Generation"
        """;

    private const string FlushSql = """
        INSERT INTO tile_version_state ("Id", "Epoch", "Flushes") VALUES (1, @epoch, 1)
        ON CONFLICT ("Id") DO UPDATE SET "Flushes" = tile_version_state."Flushes" + 1
        RETURNING "Flushes"
        """;

    private const string NotifySql = "SELECT pg_notify(@channel, @payload)";

    private const string ListenSql = "LISTEN " + Channel;

    private readonly string _connectionString;
    private readonly string _listenConnectionString;

    /// <param name="connectionString">The GeoAssets database's.</param>
    public PostgresTileVersionStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
        _listenConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            KeepAlive = KeepAliveSeconds,
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async Task<TileVersionSnapshot> LoadAsync(IReadOnlyCollection<Guid> scopes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var connection = new NpgsqlConnection(_connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var create = new NpgsqlCommand(CreateStateSql, connection);
            await using (create.ConfigureAwait(false))
            {
                create.Parameters.AddWithValue("epoch", NewEpoch());
                await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            string epoch;
            long flushes;
            var state = new NpgsqlCommand(LoadStateSql, connection);
            await using (state.ConfigureAwait(false))
            {
                var reader = await state.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    await reader.ReadAsync(ct).ConfigureAwait(false);
                    epoch = reader.GetString(0);
                    flushes = reader.GetInt64(1);
                }
            }

            var generations = new Dictionary<Guid, long>();
            var read = new NpgsqlCommand(LoadGenerationsSql, connection);
            await using (read.ConfigureAwait(false))
            {
                read.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = scopes.Distinct().ToArray() });
                var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        generations[reader.GetGuid(0)] = reader.GetInt64(1);
                }
            }

            foreach (var scope in scopes)
                generations.TryAdd(scope, 0);

            return new TileVersionSnapshot(epoch, flushes, generations);
        }
    }

    /// <inheritdoc />
    public async Task<TileVersionChange> AdvanceAsync(
        string origin, IReadOnlyCollection<Guid> scopes, bool flush, IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(regions);

        var connection = new NpgsqlConnection(_connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var generations = new Dictionary<Guid, long>();
                var ordered = scopes.Distinct().Order().ToArray();
                if (ordered.Length > 0)
                {
                    var advance = new NpgsqlCommand(AdvanceSql, connection, transaction);
                    await using (advance.ConfigureAwait(false))
                    {
                        advance.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ordered });
                        var reader = await advance.ExecuteReaderAsync(ct).ConfigureAwait(false);
                        await using (reader.ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                                generations[reader.GetGuid(0)] = reader.GetInt64(1);
                        }
                    }
                }

                long? flushes = null;
                if (flush)
                {
                    var bump = new NpgsqlCommand(FlushSql, connection, transaction);
                    await using (bump.ConfigureAwait(false))
                    {
                        bump.Parameters.AddWithValue("epoch", NewEpoch());
                        flushes = (long)(await bump.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                    }
                }

                var change = new TileVersionChange(origin, generations, flushes, [.. regions]);
                var notify = new NpgsqlCommand(NotifySql, connection, transaction);
                await using (notify.ConfigureAwait(false))
                {
                    notify.Parameters.AddWithValue("channel", Channel);
                    notify.Parameters.AddWithValue("payload", TileVersionNotification.Encode(change));
                    await notify.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return change;
            }
        }
    }

    /// <inheritdoc />
    public async Task ListenAsync(Func<CancellationToken, Task> subscribed, Func<TileVersionChange, CancellationToken, Task> changed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscribed);
        ArgumentNullException.ThrowIfNull(changed);

        var received = new ConcurrentQueue<string>();
        var connection = new NpgsqlConnection(_listenConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            connection.Notification += (_, e) =>
            {
                if (string.Equals(e.Channel, Channel, StringComparison.Ordinal)) received.Enqueue(e.Payload);
            };

            await connection.OpenAsync(ct).ConfigureAwait(false);
            var listen = new NpgsqlCommand(ListenSql, connection);
            await using (listen.ConfigureAwait(false))
            {
                await listen.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await subscribed(ct).ConfigureAwait(false);
            while (true)
            {
                while (received.TryDequeue(out var payload))
                    await changed(TileVersionNotification.Decode(payload), ct).ConfigureAwait(false);

                await connection.WaitAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static string NewEpoch() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
}
