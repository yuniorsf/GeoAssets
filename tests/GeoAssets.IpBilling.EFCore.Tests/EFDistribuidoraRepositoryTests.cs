using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>EF tests for <see cref="EFDistribuidoraRepository"/> (XD01-301).</summary>
public class EFDistribuidoraRepositoryTests
{
    private static Distribuidora New(string name) => new() { OrganizationId = Guid.NewGuid(), Name = name };

    private static async Task<List<Distribuidora>> SeedAsync(SqliteFixture fixture, params string[] names)
    {
        var repo = new EFDistribuidoraRepository(fixture.Context);
        var seeded = names.Select(New).ToList();
        foreach (var distribuidora in seeded) await repo.AddAsync(distribuidora);
        await repo.SaveChangesAsync();
        return seeded;
    }

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var fixture = new SqliteFixture();
        var seeded = (await SeedAsync(fixture, "Enel SP")).Single();

        var loaded = await new EFDistribuidoraRepository(fixture.NewContext()).GetByIdAsync(seeded.Id);

        loaded.Should().BeEquivalentTo(seeded);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var fixture = new SqliteFixture();

        (await new EFDistribuidoraRepository(fixture.Context).GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_Null_Throws()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFDistribuidoraRepository(fixture.Context);

        await repo.Invoking(r => r.AddAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await repo.Invoking(r => r.UpdateAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public async Task GetPageAsync_PageOrPageSizeBelowOne_Throws(int page, int pageSize)
    {
        using var fixture = new SqliteFixture();
        var repo = new EFDistribuidoraRepository(fixture.Context);

        await repo.Invoking(r => r.GetPageAsync(page, pageSize)).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetPageAsync_EmptyTable_ReturnsNoItemsAndZeroTotal()
    {
        using var fixture = new SqliteFixture();

        var page = await new EFDistribuidoraRepository(fixture.Context).GetPageAsync(1, 10);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPageAsync_PagesInNameOrder_AndReportsTheFullTotal()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture, "Echo", "Alpha", "Delta", "Bravo", "Charlie");
        var repo = new EFDistribuidoraRepository(fixture.NewContext());

        var first = await repo.GetPageAsync(1, 2);
        var second = await repo.GetPageAsync(2, 2);
        var third = await repo.GetPageAsync(3, 2);
        var beyond = await repo.GetPageAsync(4, 2);

        first.Items.Select(d => d.Name).Should().Equal("Alpha", "Bravo");
        second.Items.Select(d => d.Name).Should().Equal("Charlie", "Delta");
        third.Items.Select(d => d.Name).Should().Equal("Echo");
        beyond.Items.Should().BeEmpty();
        new[] { first, second, third, beyond }.Should().OnlyContain(p => p.TotalCount == 5);
    }

    [Fact]
    public async Task GetPageAsync_SameName_IsTieBrokenByIdDeterministically()
    {
        using var fixture = new SqliteFixture();
        var seeded = await SeedAsync(fixture, "Same", "Same", "Same", "Same");
        var repo = new EFDistribuidoraRepository(fixture.NewContext());

        var all = await repo.GetPageAsync(1, 10);
        var pageOne = await repo.GetPageAsync(1, 2);
        var pageTwo = await repo.GetPageAsync(2, 2);

        // SQLite stores a Guid as upper-case text, so "by Id" is that text's ordinal order.
        var expected = seeded.Select(d => d.Id)
            .OrderBy(id => id.ToString().ToUpperInvariant(), StringComparer.Ordinal).ToList();
        all.Items.Select(d => d.Id).Should().Equal(expected);
        pageOne.Items.Concat(pageTwo.Items).Select(d => d.Id).Should().Equal(expected, "pages never overlap or skip a row");
    }

    [Fact]
    public async Task UpdateAsync_ChangesPersistAfterSave()
    {
        using var fixture = new SqliteFixture();
        var seeded = (await SeedAsync(fixture, "Old name")).Single();
        var newOrganization = Guid.NewGuid();

        var repo = new EFDistribuidoraRepository(fixture.NewContext());
        await repo.UpdateAsync(new Distribuidora { Id = seeded.Id, OrganizationId = newOrganization, Name = "New name" });
        await repo.SaveChangesAsync();

        var loaded = await new EFDistribuidoraRepository(fixture.NewContext()).GetByIdAsync(seeded.Id);
        loaded!.Name.Should().Be("New name");
        loaded.OrganizationId.Should().Be(newOrganization);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_IsANoOp()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFDistribuidoraRepository(fixture.Context);

        await repo.UpdateAsync(New("Ghost"));
        await repo.SaveChangesAsync();

        (await repo.GetPageAsync(1, 10)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow_AndUnknownIdIsANoOp()
    {
        using var fixture = new SqliteFixture();
        var seeded = (await SeedAsync(fixture, "Enel SP")).Single();
        var repo = new EFDistribuidoraRepository(fixture.NewContext());

        await repo.DeleteAsync(Guid.NewGuid());
        await repo.DeleteAsync(seeded.Id);
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync(seeded.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_CascadesToTheChart_AndDetachesItsMunicipiosInsteadOfDeletingThem()
    {
        using var fixture = new SqliteFixture();
        var seeded = (await SeedAsync(fixture, "Enel SP")).Single();
        var chart = new EFDistribuidoraLampWattageRepository(fixture.Context);
        await chart.UpsertAsync(new DistribuidoraLampWattage { DistribuidoraId = seeded.Id, Code = LampTypeCode.VS, WattageW = 100, BallastLossW = 17m });
        await chart.SaveChangesAsync();
        var municipios = new EFMunicipioRepository(fixture.Context);
        var municipio = new Municipio
        {
            IbgeCode = "3550308",
            Name = "São Paulo",
            Uf = "SP",
            DistribuidoraOrganizationId = Guid.NewGuid(),
            DistribuidoraId = seeded.Id,
        };
        await municipios.AddAsync(municipio);
        await municipios.SaveChangesAsync();

        var repo = new EFDistribuidoraRepository(fixture.NewContext());
        await repo.DeleteAsync(seeded.Id);
        await repo.SaveChangesAsync();

        var fresh = fixture.NewContext();
        (await new EFDistribuidoraLampWattageRepository(fresh).GetPageAsync(seeded.Id, 1, 10)).TotalCount.Should().Be(0);
        var reloaded = await new EFMunicipioRepository(fresh).GetByIdAsync(municipio.Id);
        reloaded.Should().NotBeNull("deleting a Distribuidora must never delete its municípios");
        reloaded!.DistribuidoraId.Should().BeNull();
    }

    [Fact]
    public async Task Municipio_DistribuidoraId_RoundTripsThroughAddAndUpdate()
    {
        using var fixture = new SqliteFixture();
        var seeded = await SeedAsync(fixture, "Enel SP", "CPFL");
        var municipio = new Municipio
        {
            IbgeCode = "3550308",
            Name = "São Paulo",
            Uf = "SP",
            DistribuidoraOrganizationId = Guid.NewGuid(),
            DistribuidoraId = seeded[0].Id,
        };
        var repo = new EFMunicipioRepository(fixture.Context);
        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();

        (await new EFMunicipioRepository(fixture.NewContext()).GetByIdAsync(municipio.Id))!.DistribuidoraId.Should().Be(seeded[0].Id);

        municipio.DistribuidoraId = seeded[1].Id;
        var updater = new EFMunicipioRepository(fixture.NewContext());
        await updater.UpdateAsync(municipio);
        await updater.SaveChangesAsync();
        (await new EFMunicipioRepository(fixture.NewContext()).GetByIdAsync(municipio.Id))!.DistribuidoraId.Should().Be(seeded[1].Id);

        municipio.DistribuidoraId = null;
        var clearer = new EFMunicipioRepository(fixture.NewContext());
        await clearer.UpdateAsync(municipio);
        await clearer.SaveChangesAsync();
        (await new EFMunicipioRepository(fixture.NewContext()).GetByIdAsync(municipio.Id))!.DistribuidoraId.Should().BeNull();
    }
}
