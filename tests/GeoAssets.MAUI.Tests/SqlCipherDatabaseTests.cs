using System.Text;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.MAUI.Services.Offline;
using Microsoft.Extensions.Time.Testing;
using SQLite;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-461 — the app's local database (<c>LocalOfflineDatabase</c>) over the SQLCipher package the app ships:
/// <see cref="SqlCipherDatabase"/> makes the key actually encrypt the file (sqlite-net-pcl used to replace SQLCipher
/// with plain SQLite, which ignores it), encrypts the plaintext database an earlier build left, and an existing
/// install's tile table written before XD01-438 migrates with its data kept. Each test opens the database the way
/// <c>LocalOfflineDatabase.GetConnectionAsync</c> does.
/// </summary>
public sealed class SqlCipherDatabaseTests : IDisposable
{
    private const string Key = "dGhlLWRldmljZS1rZXktZnJvbS1zZWN1cmUtc3RvcmFnZQ==";
    private const string Marker = "pole-census-marker-7f3a";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-maui-sqlcipher-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public SqlCipherDatabaseTests()
    {
        SqlCipherDatabase.EnsureProvider();
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "geoassets-offline.db3");
    }

    public void Dispose()
    {
        SQLiteAsyncConnection.ResetPool();
        Directory.Delete(_directory, recursive: true);
    }

    private SQLiteAsyncConnection OpenKeyed(string? key = Key) =>
        new(new SQLiteConnectionString(_path, storeDateTimeAsTicks: true, key: key));

    private bool FileContainsMarker() => Encoding.ASCII.GetString(File.ReadAllBytes(_path)).Contains(Marker, StringComparison.Ordinal);

    [Fact]
    public void TheProvider_IsSqlCipher_EvenAfterSqliteNetChoseItsOwn()
    {
        new SQLiteConnection(Path.Combine(_directory, "other.db3")).Dispose();

        SqlCipherDatabase.EnsureProvider().Should().Be("e_sqlcipher");
        SQLitePCL.raw.GetNativeLibraryName().Should().Be("e_sqlcipher", "opening a connection must not bring sqlite-net's plain SQLite back");
    }

    [Fact]
    public async Task AKeyedDatabase_IsEncryptedOnDisk_AndUnreadableWithoutItsKey()
    {
        var db = OpenKeyed();
        await SqlCipherDatabase.RequireEncryptionAsync(db);
        await db.CreateTableAsync<LocalVectorTileRow>();
        await db.InsertAsync(new LocalVectorTileRow { Key = "1:2:3", Z = 1, X = 2, Y = 3, Data = Encoding.ASCII.GetBytes(Marker) });
        await db.CloseAsync();

        SqlCipherDatabase.IsPlaintext(_path).Should().BeFalse();
        FileContainsMarker().Should().BeFalse("the row must not be readable in the file");

        var reopened = OpenKeyed();
        (await reopened.FindAsync<LocalVectorTileRow>("1:2:3"))!.Data.Should().Equal(Encoding.ASCII.GetBytes(Marker));
        await reopened.CloseAsync();

        var withoutKey = new SQLiteAsyncConnection(_path, storeDateTimeAsTicks: true);
        var read = () => withoutKey.Table<LocalVectorTileRow>().CountAsync();
        await read.Should().ThrowAsync<SQLiteException>().WithMessage("*not a database*");
        await withoutKey.CloseAsync();

        var wrongKey = OpenKeyed("another-device-key");
        var readWrong = () => wrongKey.Table<LocalVectorTileRow>().CountAsync();
        await readWrong.Should().ThrowAsync<SQLiteException>().WithMessage("*not a database*");
        await wrongKey.CloseAsync();
    }

    [Fact]
    public async Task APlaintextDatabase_IsEncryptedInPlace_WithEveryRowKept()
    {
        await WritePlaintextPreXd01438DatabaseAsync();
        SqlCipherDatabase.IsPlaintext(_path).Should().BeTrue();
        FileContainsMarker().Should().BeTrue();

        SqlCipherDatabase.EncryptIfPlaintext(_path, Key).Should().BeTrue();

        SqlCipherDatabase.IsPlaintext(_path).Should().BeFalse();
        FileContainsMarker().Should().BeFalse();
        File.Exists(_path + ".encrypting").Should().BeFalse();
        var db = OpenKeyed();
        await SqlCipherDatabase.RequireEncryptionAsync(db);
        (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_vector_tiles")).Should().Be(2);
        (await db.ExecuteScalarAsync<string>("SELECT Payload FROM local_outbox WHERE Id = 7")).Should().Be(Marker);
        await db.CloseAsync();

        SqlCipherDatabase.EncryptIfPlaintext(_path, Key).Should().BeFalse("an encrypted database is left as it is");
    }

    [Fact]
    public async Task AnExistingInstall_FromBeforeXd01438_OpensMigratesAndKeepsItsTiles()
    {
        // The device's database as a build before XD01-438 (and before XD01-461) left it: plaintext, and the tile table
        // without ContentType/ContentEncoding, holding basemap tiles.
        await WritePlaintextPreXd01438DatabaseAsync();

        // What LocalOfflineDatabase.GetConnectionAsync now does on the upgraded app's first start.
        SqlCipherDatabase.EncryptIfPlaintext(_path, Key);
        var db = OpenKeyed();
        await SqlCipherDatabase.RequireEncryptionAsync(db);
        var migration = await db.CreateTableAsync<LocalVectorTileRow>();

        migration.Should().Be(CreateTableResult.Migrated);
        (await db.GetTableInfoAsync("local_vector_tiles")).Select(c => c.Name)
            .Should().Contain(["ContentType", "ContentEncoding"]);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        var cache = new SqliteLocalVectorTileCache(() => Task.FromResult(db), clock);
        var basemap = cache;
        var kept = await basemap.GetAsync(5, 10, 12);
        kept!.Data.Should().Equal(Encoding.ASCII.GetBytes(Marker));
        kept.CachedAtUtc.Should().Be(new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc));
        (await basemap.GetAsync(6, 20, 24))!.Data.Should().Equal(4, 5, 6);

        var key = new TileCacheKey(new TileCacheScope("offline-pack", "pmtiles"), "pmtiles", "0123456789abcdef", new TileAddress(5, 10, 12), TileFormat.Mvt);
        await cache.SetAsync(key, new TilePayload(new byte[] { 1, 2 }, TileFormats.MapboxVectorTileContentType, "gzip"), CancellationToken.None);
        var tile = await cache.GetAsync(key, CancellationToken.None);
        tile!.ContentEncoding.Should().Be("gzip");
        (await basemap.GetAsync(5, 10, 12))!.Data.Should().Equal(Encoding.ASCII.GetBytes(Marker), "a tile cache entry never overwrites a basemap one");
        (await db.ExecuteScalarAsync<string>("SELECT Payload FROM local_outbox WHERE Id = 7")).Should().Be(Marker);

        (await db.CreateTableAsync<LocalVectorTileRow>()).Should().Be(CreateTableResult.Migrated, "sqlite-net answers Migrated for an existing table");
        (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_vector_tiles")).Should().Be(3, "running the migration again changes nothing");
        await db.CloseAsync();
        FileContainsMarker().Should().BeFalse();
    }

    [Fact]
    public void AMissingDatabase_IsNotCreated()
    {
        SqlCipherDatabase.EncryptIfPlaintext(_path, Key).Should().BeFalse();

        File.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public async Task ALeftoverOfAnInterruptedEncryption_IsReplaced()
    {
        await WritePlaintextPreXd01438DatabaseAsync();
        await File.WriteAllBytesAsync(_path + ".encrypting", [1, 2, 3]);

        SqlCipherDatabase.EncryptIfPlaintext(_path, Key).Should().BeTrue();

        File.Exists(_path + ".encrypting").Should().BeFalse();
        var db = OpenKeyed();
        (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_vector_tiles")).Should().Be(2);
        await db.CloseAsync();
    }

    [Fact]
    public async Task AFailedEncryption_LeavesTheFileAsItWas_AndNoPartialCopy()
    {
        // SQLite's header over a damaged body: recognised as plaintext, but its export fails.
        var damaged = new byte[8192];
        Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(damaged, 0);
        damaged.AsSpan(16).Fill(0xA5);
        await File.WriteAllBytesAsync(_path, damaged);

        var act = () => SqlCipherDatabase.EncryptIfPlaintext(_path, Key);

        act.Should().Throw<SQLiteException>();
        (await File.ReadAllBytesAsync(_path)).Should().Equal(damaged);
        File.Exists(_path + ".encrypting").Should().BeFalse();
    }

    private async Task WritePlaintextPreXd01438DatabaseAsync()
    {
        var db = new SQLiteAsyncConnection(_path, storeDateTimeAsTicks: true);
        await db.CreateTableAsync<PreXd01438LocalVectorTileRow>();
        await db.InsertAsync(new PreXd01438LocalVectorTileRow
        {
            Key = "5:10:12", Z = 5, X = 10, Y = 12, Data = Encoding.ASCII.GetBytes(Marker),
            CachedAtUtc = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc),
        });
        await db.InsertAsync(new PreXd01438LocalVectorTileRow
        {
            Key = "6:20:24", Z = 6, X = 20, Y = 24, Data = [4, 5, 6],
            CachedAtUtc = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
        });
        await db.ExecuteAsync("CREATE TABLE local_outbox (Id INTEGER PRIMARY KEY, Payload TEXT)");
        await db.ExecuteAsync("INSERT INTO local_outbox (Id, Payload) VALUES (7, ?)", Marker);
        await db.CloseAsync();
    }

    /// <summary><c>LocalVectorTileRow</c> as XD01-243 shipped it, before XD01-438 added ContentType/ContentEncoding.</summary>
    [Table("local_vector_tiles")]
    private sealed class PreXd01438LocalVectorTileRow
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;

        public int Z { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public byte[] Data { get; set; } = [];
        public DateTime CachedAtUtc { get; set; }
    }
}
