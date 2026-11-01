using SQLite;
using SQLitePCL;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// SQLCipher-encrypted local database (XD01-243) shared by every <c>Sqlite*</c> store in this
/// folder — one file, one connection, one key. The key is a random 256-bit value generated once
/// per device and cached in platform-native secure storage (Keychain on iOS/MacCatalyst,
/// Keystore-backed on Android) via <see cref="SecureStorage"/>; it is never written to disk in
/// plaintext and never logged.
///
/// Package choice caveat (flagged, not silently assumed): <c>SQLitePCLRaw.bundle_e_sqlcipher</c>
/// is the de-facto community package for SQLCipher-via-sqlite-net-pcl on MAUI — there is no
/// first-party Microsoft package for this. NuGet's own metadata flags the package as having had
/// no recent release; it is still the functional, widely-used option. This could not be
/// runtime-verified on a device/simulator in this environment (same caveat as XD01-52/121) — only
/// build-verified across the android/ios/maccatalyst TFMs.
/// </summary>
internal static class LocalOfflineDatabase
{
    private const string SecureStorageKeyName = "geoassets.offline.dbkey";
    private const string DatabaseFileName = "geoassets-offline.db3";

    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static SQLiteAsyncConnection? _connection;

    static LocalOfflineDatabase()
    {
        // Registers the e_sqlcipher native provider bundled with SQLitePCLRaw.bundle_e_sqlcipher —
        // the one, standard init call every SQLitePCLRaw bundle package documents.
        Batteries_V2.Init();
    }

    public static async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection is not null) return _connection;

            var key = await GetOrCreateKeyAsync().ConfigureAwait(false);
            var path = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
            var connectionString = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
            var connection = new SQLiteAsyncConnection(connectionString);

            await connection.CreateTableAsync<LocalServiceOrderRow>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalBaseSnapshotRow>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalOutboxRow>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalSyncCursorRow>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalMediaStagingRow>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalVectorTileRow>().ConfigureAwait(false);

            _connection = connection;
            return connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<string> GetOrCreateKeyAsync()
    {
        var existing = await SecureStorage.Default.GetAsync(SecureStorageKeyName).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existing)) return existing;

        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var key = Convert.ToBase64String(keyBytes);
        await SecureStorage.Default.SetAsync(SecureStorageKeyName, key).ConfigureAwait(false);
        return key;
    }
}
