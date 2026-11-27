using System.Runtime.CompilerServices;
using SQLite;
using SQLitePCL;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// The SQLCipher side of <see cref="LocalOfflineDatabase"/> (XD01-461), free of MAUI types so it is tested on net10.0.
/// <para>
/// sqlite-net-pcl 1.11 chooses its own plain SQLite provider (e_sqlite3; the system SQLite on iOS) in
/// <see cref="SQLiteConnection"/>'s static constructor, which runs after <c>Batteries_V2.Init()</c> chose SQLCipher, so
/// until XD01-461 the key was an unknown <c>PRAGMA key</c> that SQLite silently ignores and the database was written in
/// plaintext. <see cref="EnsureProvider"/> runs that constructor first and only then selects SQLCipher;
/// <see cref="RequireEncryptionAsync"/> refuses a connection that isn't SQLCipher's; <see cref="EncryptIfPlaintext"/>
/// encrypts, once and in place, the plaintext database an earlier build left on the device.
/// </para>
/// </summary>
internal static class SqlCipherDatabase
{
    private static readonly byte[] PlaintextHeader = "SQLite format 3\0"u8.ToArray();

    private static readonly Lazy<string> Provider = new(() =>
    {
        RuntimeHelpers.RunClassConstructor(typeof(SQLiteConnection).TypeHandle);
        Batteries_V2.Init();
        return raw.GetNativeLibraryName();
    });

    /// <summary>Makes SQLCipher the SQLite every connection of the process opens; answers its native library's name.</summary>
    public static string EnsureProvider() => Provider.Value;

    /// <summary>Whether <paramref name="path"/> is an unencrypted SQLite database (its file starts with SQLite's header).</summary>
    public static bool IsPlaintext(string path)
    {
        var header = new byte[PlaintextHeader.Length];
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
               && header.AsSpan().SequenceEqual(PlaintextHeader);
    }

    /// <summary>
    /// Encrypts the plaintext database at <paramref name="path"/> with <paramref name="key"/> (the passphrase
    /// <see cref="SQLiteConnectionString"/>'s key is): its schema and rows are exported into a new encrypted file, which
    /// then replaces it. A missing or already encrypted file is left as it is.
    /// </summary>
    /// <returns>Whether the database was encrypted.</returns>
    public static bool EncryptIfPlaintext(string path, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrEmpty(key);
        EnsureProvider();
        if (!File.Exists(path) || !IsPlaintext(path))
            return false;

        var encrypted = path + ".encrypting";
        File.Delete(encrypted);
        try
        {
            using (var plaintext = new SQLiteConnection(path, storeDateTimeAsTicks: true))
            {
                plaintext.Execute("ATTACH DATABASE ? AS encrypted KEY ?", encrypted, key);
                plaintext.ExecuteScalar<string>("SELECT sqlcipher_export('encrypted')");
                plaintext.Execute("DETACH DATABASE encrypted");
            }

            File.Move(encrypted, path, overwrite: true);
            return true;
        }
        catch
        {
            File.Delete(encrypted);
            throw;
        }
    }

    /// <summary>Throws unless <paramref name="connection"/> is a SQLCipher connection (so its key encrypts the file).</summary>
    /// <exception cref="InvalidOperationException">The connection's SQLite is not SQLCipher.</exception>
    public static async Task RequireEncryptionAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var version = await connection.ExecuteScalarAsync<string>("PRAGMA cipher_version").ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
            throw new InvalidOperationException(
                $"The offline database's SQLite ({raw.GetNativeLibraryName()}) is not SQLCipher; refusing to store data unencrypted.");
    }
}
