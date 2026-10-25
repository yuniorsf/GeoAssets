using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFFaturamentoRepositoryTests
{
    private static async Task<Municipio> SeedMunicipioAsync(IpBillingDbContext db)
    {
        var repo = new EFMunicipioRepository(db);
        var municipio = new Municipio
        {
            IbgeCode                    = "3550308",
            Name                        = "São Paulo",
            Uf                          = "SP",
            DistribuidoraOrganizationId = Guid.NewGuid(),
        };
        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();
        return municipio;
    }

    [Fact]
    public async Task AddAsync_ThenGetByMunicipioAndCompetencia_RoundTripsAllFields()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFFaturamentoRepository(fixture.Context);
        var faturamento = new Faturamento
        {
            MunicipioId = municipio.Id,
            Competencia = "2026-03",
            KwhFaturado = 12345.6789m,
            CreatedAt   = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        };

        await repo.AddAsync(faturamento);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByMunicipioAndCompetenciaAsync(municipio.Id, "2026-03");
        loaded.Should().BeEquivalentTo(faturamento);
    }

    [Fact]
    public async Task GetByMunicipioAndCompetenciaAsync_NoMatch_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFFaturamentoRepository(fixture.Context);

        var loaded = await repo.GetByMunicipioAndCompetenciaAsync(municipio.Id, "2026-03");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetByMunicipioAsync_ReturnsEveryCompetenciaOrdered()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFFaturamentoRepository(fixture.Context);

        await repo.AddAsync(new Faturamento { MunicipioId = municipio.Id, Competencia = "2026-02", KwhFaturado = 100m, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) });
        await repo.AddAsync(new Faturamento { MunicipioId = municipio.Id, Competencia = "2026-01", KwhFaturado = 90m, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) });
        await repo.SaveChangesAsync();

        var all = await repo.GetByMunicipioAsync(municipio.Id);

        all.Select(f => f.Competencia).Should().Equal("2026-01", "2026-02");
    }

    [Fact]
    public async Task AddAsync_DuplicateMunicipioAndCompetencia_ViolatesUniqueIndex()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFFaturamentoRepository(fixture.Context);
        await repo.AddAsync(new Faturamento { MunicipioId = municipio.Id, Competencia = "2026-03", KwhFaturado = 100m, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) });
        await repo.SaveChangesAsync();

        await repo.AddAsync(new Faturamento { MunicipioId = municipio.Id, Competencia = "2026-03", KwhFaturado = 200m, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) });
        var act = async () => await repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingMunicipioWithFaturamentoRows_IsRestrictedByTheForeignKey()
    {
        // Proves FaturamentoRecordConfiguration's FK is Restrict, not Cascade — billing
        // history must never silently disappear because its município row was deleted.
        //
        // The delete below goes through a *fresh* DbContext (fixture.NewContext()) rather than
        // fixture.Context — with the FaturamentoRecord already tracked in the same change
        // tracker as the Municipio being removed, EF Core detects the severed required
        // relationship client-side and throws InvalidOperationException before ever reaching
        // the database. A separate context has no such tracked row, so the DELETE is sent for
        // real and the database's own FK constraint is what rejects it (DbUpdateException) —
        // matching how two independent requests would actually behave in production.
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var faturamentoRepo = new EFFaturamentoRepository(fixture.Context);
        await faturamentoRepo.AddAsync(new Faturamento
        {
            MunicipioId = municipio.Id, Competencia = "2026-03", KwhFaturado = 100m, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        });
        await faturamentoRepo.SaveChangesAsync();

        var municipioRepo = new EFMunicipioRepository(fixture.NewContext());
        await municipioRepo.DeleteAsync(municipio.Id);
        var act = async () => await municipioRepo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
