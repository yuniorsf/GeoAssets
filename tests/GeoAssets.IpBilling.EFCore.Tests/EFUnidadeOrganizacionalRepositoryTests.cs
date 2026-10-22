using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFUnidadeOrganizacionalRepositoryTests
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
    public async Task AddAsync_ThenGetByMunicipio_RoundTripsAllFields()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFUnidadeOrganizacionalRepository(fixture.Context);
        var unidade = new UnidadeOrganizacional { MunicipioId = municipio.Id, Name = "Zona Norte", Code = "ZN" };

        await repo.AddAsync(unidade);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(unidade.Id);
        loaded.Should().BeEquivalentTo(unidade);

        var byMunicipio = await repo.GetByMunicipioAsync(municipio.Id);
        byMunicipio.Should().ContainSingle(u => u.Id == unidade.Id);
    }

    [Fact]
    public async Task UpdateAsync_ChangesPersistAfterSave()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFUnidadeOrganizacionalRepository(fixture.Context);
        var unidade = new UnidadeOrganizacional { MunicipioId = municipio.Id, Name = "Zona Norte" };
        await repo.AddAsync(unidade);
        await repo.SaveChangesAsync();

        unidade.Name = "Zona Norte (renamed)";
        await repo.UpdateAsync(unidade);
        await repo.SaveChangesAsync();

        var reloaded = await new EFUnidadeOrganizacionalRepository(fixture.NewContext()).GetByIdAsync(unidade.Id);
        reloaded!.Name.Should().Be("Zona Norte (renamed)");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var repo = new EFUnidadeOrganizacionalRepository(fixture.Context);
        var unidade = new UnidadeOrganizacional { MunicipioId = municipio.Id, Name = "Zona Norte" };
        await repo.AddAsync(unidade);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(unidade.Id);
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync(unidade.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeletingOwningMunicipio_CascadesToItsUnidades()
    {
        // Proves MunicipioRecordConfiguration/UnidadeOrganizacionalRecordConfiguration's FK is
        // wired Cascade, not Restrict — deleting a Municipio is expected to take its reporting
        // subdivisions with it (unlike Faturamento's FK, which is deliberately Restrict).
        using var fixture = new SqliteFixture();
        var municipio = await SeedMunicipioAsync(fixture.Context);
        var unidadeRepo = new EFUnidadeOrganizacionalRepository(fixture.Context);
        var unidade = new UnidadeOrganizacional { MunicipioId = municipio.Id, Name = "Zona Norte" };
        await unidadeRepo.AddAsync(unidade);
        await unidadeRepo.SaveChangesAsync();

        var municipioRepo = new EFMunicipioRepository(fixture.Context);
        await municipioRepo.DeleteAsync(municipio.Id);
        await municipioRepo.SaveChangesAsync();

        (await unidadeRepo.GetByIdAsync(unidade.Id)).Should().BeNull();
    }
}
