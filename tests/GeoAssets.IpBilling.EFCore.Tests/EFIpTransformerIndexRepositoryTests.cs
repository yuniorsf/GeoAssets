using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFIpTransformerIndexRepositoryTests
{
    private static IpTransformerIndex FullIndex() => new()
    {
        MunicipioIbgeCode    = "3550308",
        CodigoInstalacao     = "INST-0001",
        TransformerFeatureId = Guid.NewGuid(),
    };

    [Fact]
    public async Task AddAsync_ThenGetAsync_RoundTripsAllFields()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFIpTransformerIndexRepository(fixture.Context);
        var index = FullIndex();

        await repo.AddAsync(index);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetAsync("3550308", "INST-0001");
        loaded.Should().BeEquivalentTo(index);
    }

    [Fact]
    public async Task GetAsync_NoMatch_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFIpTransformerIndexRepository(fixture.Context);

        var loaded = await repo.GetAsync("0000000", "NOPE");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetByMunicipioAsync_ReturnsOnlyRowsForThatMunicipio()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFIpTransformerIndexRepository(fixture.Context);

        await repo.AddAsync(FullIndex());
        await repo.AddAsync(new IpTransformerIndex
        {
            MunicipioIbgeCode    = "3304557",
            CodigoInstalacao     = "INST-0001",
            TransformerFeatureId = Guid.NewGuid(),
        });
        await repo.SaveChangesAsync();

        var result = await repo.GetByMunicipioAsync("3550308");

        result.Should().ContainSingle(i => i.CodigoInstalacao == "INST-0001");
    }

    [Fact]
    public async Task AddAsync_DuplicateMunicipioAndCodigoInstalacao_ViolatesUniqueIndex()
    {
        // Proves IpTransformerIndexRecordConfiguration's unique composite index is actually
        // enforced — this lookup table's whole purpose is a 1:1 (município, código de
        // instalação) → transformer mapping.
        using var fixture = new SqliteFixture();
        var repo = new EFIpTransformerIndexRepository(fixture.Context);
        await repo.AddAsync(FullIndex());
        await repo.SaveChangesAsync();

        await repo.AddAsync(FullIndex());
        var act = async () => await repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_ChangesPersistAfterSave()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFIpTransformerIndexRepository(fixture.Context);
        var index = FullIndex();
        await repo.AddAsync(index);
        await repo.SaveChangesAsync();

        var newFeatureId = Guid.NewGuid();
        index.TransformerFeatureId = newFeatureId;
        await repo.UpdateAsync(index);
        await repo.SaveChangesAsync();

        var reloaded = await new EFIpTransformerIndexRepository(fixture.NewContext())
            .GetAsync("3550308", "INST-0001");
        reloaded!.TransformerFeatureId.Should().Be(newFeatureId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFIpTransformerIndexRepository(fixture.Context);
        var index = FullIndex();
        await repo.AddAsync(index);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(index.Id);
        await repo.SaveChangesAsync();

        (await repo.GetAsync("3550308", "INST-0001")).Should().BeNull();
    }
}
