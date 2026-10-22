using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFMunicipioRepositoryTests
{
    private static Municipio FullMunicipio() => new()
    {
        IbgeCode                    = "3550308",
        Name                        = "São Paulo",
        Uf                          = "SP",
        DistribuidoraOrganizationId = Guid.NewGuid(),
        PrefeituraOrganizationId    = Guid.NewGuid(),
        OrigemLevantamento          = OrigemWorkflow.Planilha,
        OrigemFiscalizacao          = OrigemWorkflow.Planilha,
        OrigemOficio                = OrigemWorkflow.OrdemServico,
        OrigemMemoriaCalculo        = OrigemWorkflow.Planilha,
    };

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);
        var municipio = FullMunicipio();

        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(municipio.Id);

        loaded.Should().BeEquivalentTo(municipio);
    }

    [Fact]
    public async Task GetByIbgeCodeAsync_ExistingCode_ReturnsMunicipio()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);
        var municipio = FullMunicipio();

        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIbgeCodeAsync("3550308");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(municipio.Id);
    }

    [Fact]
    public async Task GetByIbgeCodeAsync_MissingCode_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);

        var loaded = await repo.GetByIbgeCodeAsync("0000000");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryMunicipioOrderedByName()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);

        await repo.AddAsync(new Municipio
        {
            IbgeCode                    = "3304557",
            Name                        = "Rio de Janeiro",
            Uf                          = "RJ",
            DistribuidoraOrganizationId = Guid.NewGuid(),
        });
        await repo.AddAsync(FullMunicipio());
        await repo.SaveChangesAsync();

        var all = await repo.GetAllAsync();

        all.Select(m => m.Name).Should().Equal("Rio de Janeiro", "São Paulo");
    }

    [Fact]
    public async Task UpdateAsync_ChangesPersistAfterSave()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);
        var municipio = FullMunicipio();
        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();

        municipio.Name = "São Paulo (updated)";
        municipio.OrigemOficio = OrigemWorkflow.Planilha;
        await repo.UpdateAsync(municipio);
        await repo.SaveChangesAsync();

        var reloaded = await new EFMunicipioRepository(fixture.NewContext()).GetByIdAsync(municipio.Id);
        reloaded!.Name.Should().Be("São Paulo (updated)");
        reloaded.OrigemOficio.Should().Be(OrigemWorkflow.Planilha);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);
        var municipio = FullMunicipio();
        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(municipio.Id);
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync(municipio.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFMunicipioRepository(fixture.Context);

        var act = async () => { await repo.DeleteAsync(Guid.NewGuid()); await repo.SaveChangesAsync(); };

        await act.Should().NotThrowAsync();
    }
}
