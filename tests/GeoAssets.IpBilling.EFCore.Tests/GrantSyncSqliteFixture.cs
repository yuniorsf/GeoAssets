using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// A file-backed SQLite database (XD01-395): unlike <see cref="SqliteFixture"/>'s single held-open connection, every
/// <see cref="NewContext"/> gets a connection of its own, so two claimers can really run side by side. Time is a
/// <see cref="FakeTimeProvider"/>.
/// </summary>
internal sealed class GrantSyncSqliteFixture : IDisposable
{
    public static readonly Guid DistribuidoraOrg = new("d0d0d0d0-0000-4000-8000-000000000395");
    public static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000395");
    public static readonly Guid PrefeituraB = new("b0b0b0b0-0000-4000-8000-000000000395");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"grant-sync-{Guid.NewGuid():N}.db");

    public GrantSyncSqliteFixture()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    public IpBillingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<IpBillingDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);

    public EFMunicipioGrantSyncOutbox NewOutbox(IpBillingDbContext db) => new(db, Time);

    public static MunicipioGrantChange Sync(Guid municipioId, string ibge = "3550308", Guid? prefeitura = null) =>
        new(municipioId, MunicipioGrantSyncKind.Sync, ibge, DistribuidoraOrg, prefeitura ?? PrefeituraA);

    /// <summary>Queues <paramref name="change"/> on its own context and commits it, with no município save.</summary>
    public async Task<long> QueueAsync(MunicipioGrantChange change, string requestedBy = "requester-1")
    {
        await using var db = NewContext();
        NewOutbox(db).Enqueue(change, requestedBy);
        await db.SaveChangesAsync();
        return await db.MunicipioGrantSyncRequests.MaxAsync(r => r.Id);
    }

    public async Task<Municipio> AddMunicipioAsync(string ibge, Guid? prefeitura = null)
    {
        await using var db = NewContext();
        var municipio = new Municipio { IbgeCode = ibge, Name = $"M{ibge}", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg, PrefeituraOrganizationId = prefeitura };
        await new EFMunicipioRepository(db).AddAsync(municipio);
        await db.SaveChangesAsync();
        return municipio;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // best-effort cleanup of the temp database
            }
        }
    }
}
