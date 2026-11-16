using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>EF tests for <see cref="EFDistribuidoraLampWattageRepository"/> (XD01-301).</summary>
public class EFDistribuidoraLampWattageRepositoryTests
{
    private static async Task<Guid> SeedDistribuidoraAsync(SqliteFixture fixture, string name = "Enel SP")
    {
        var repo = new EFDistribuidoraRepository(fixture.Context);
        var distribuidora = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = name };
        await repo.AddAsync(distribuidora);
        await repo.SaveChangesAsync();
        return distribuidora.Id;
    }

    private static DistribuidoraLampWattage Row(Guid distribuidoraId, LampTypeCode code, int wattageW, decimal ballastLossW) => new()
    {
        DistribuidoraId = distribuidoraId,
        Code = code,
        WattageW = wattageW,
        BallastLossW = ballastLossW,
    };

    private static async Task SeedChartAsync(
        SqliteFixture fixture, Guid distribuidoraId, params (LampTypeCode Code, int WattageW, decimal BallastLossW)[] rows)
    {
        var repo = new EFDistribuidoraLampWattageRepository(fixture.Context);
        foreach (var (code, wattageW, ballastLossW) in rows)
        {
            await repo.UpsertAsync(Row(distribuidoraId, code, wattageW, ballastLossW));
        }

        await repo.SaveChangesAsync();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task GetPageAsync_PageOrPageSizeBelowOne_Throws(int page, int pageSize)
    {
        using var fixture = new SqliteFixture();
        var repo = new EFDistribuidoraLampWattageRepository(fixture.Context);

        await repo.Invoking(r => r.GetPageAsync(Guid.NewGuid(), page, pageSize))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetPageAsync_SortsByCodeThenWattage_PagesAndScopesToTheDistribuidora()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        var cpfl = await SeedDistribuidoraAsync(fixture, "CPFL");
        await SeedChartAsync(fixture, enel,
            (LampTypeCode.VS, 250, 30m), (LampTypeCode.LD, 150, 0m), (LampTypeCode.VS, 70, 14m),
            (LampTypeCode.FL, 40, 8m), (LampTypeCode.LD, 60, 0m));
        await SeedChartAsync(fixture, cpfl, (LampTypeCode.FL, 20, 5m));
        var repo = new EFDistribuidoraLampWattageRepository(fixture.NewContext());

        var first = await repo.GetPageAsync(enel, 1, 3);
        var second = await repo.GetPageAsync(enel, 2, 3);
        var beyond = await repo.GetPageAsync(enel, 3, 3);

        first.Items.Select(r => (r.Code, r.WattageW)).Should().Equal(
            (LampTypeCode.FL, 40), (LampTypeCode.LD, 60), (LampTypeCode.LD, 150));
        second.Items.Select(r => (r.Code, r.WattageW)).Should().Equal(
            (LampTypeCode.VS, 70), (LampTypeCode.VS, 250));
        beyond.Items.Should().BeEmpty();
        new[] { first, second, beyond }.Should().OnlyContain(p => p.TotalCount == 5, "CPFL's row is not counted");
        first.Items.Should().OnlyContain(r => r.DistribuidoraId == enel);
    }

    [Fact]
    public async Task GetPageAsync_UnknownDistribuidora_ReturnsEmptyPage()
    {
        using var fixture = new SqliteFixture();

        var page = await new EFDistribuidoraLampWattageRepository(fixture.Context).GetPageAsync(Guid.NewGuid(), 1, 10);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task FindAsync_MatchesExactlyCodeAndWattage_WithinTheDistribuidora()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        var cpfl = await SeedDistribuidoraAsync(fixture, "CPFL");
        await SeedChartAsync(fixture, enel, (LampTypeCode.VS, 70, 14m));
        await SeedChartAsync(fixture, cpfl, (LampTypeCode.VS, 100, 17m));
        var repo = new EFDistribuidoraLampWattageRepository(fixture.NewContext());

        var found = await repo.FindAsync(enel, LampTypeCode.VS, 70);

        found.Should().NotBeNull();
        found!.BallastLossW.Should().Be(14m);
        found.TotalBilledW.Should().Be(84m);
        (await repo.FindAsync(enel, LampTypeCode.VS, 100)).Should().BeNull("that row belongs to another Distribuidora");
        (await repo.FindAsync(enel, LampTypeCode.LD, 70)).Should().BeNull();
        (await repo.FindAsync(enel, LampTypeCode.VS, 71)).Should().BeNull();
    }

    [Fact]
    public async Task GetMeanTotalBilledWAsync_EmptyChart_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        var cpfl = await SeedDistribuidoraAsync(fixture, "CPFL");
        await SeedChartAsync(fixture, cpfl, (LampTypeCode.VS, 100, 17m));
        var repo = new EFDistribuidoraLampWattageRepository(fixture.NewContext());

        (await repo.GetMeanTotalBilledWAsync(enel)).Should().BeNull("another Distribuidora's rows do not count");
        (await repo.GetMeanTotalBilledWAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetMeanTotalBilledWAsync_ReturnsTheUnroundedMeanOfWattagePlusBallastLoss()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        // 84 + 117 + 60.5 = 261.5 → mean 87.1666… (not rounded to the ledger's 4 decimals).
        await SeedChartAsync(fixture, enel, (LampTypeCode.VS, 70, 14m), (LampTypeCode.VS, 100, 17m), (LampTypeCode.LD, 60, 0.5m));

        var mean = await new EFDistribuidoraLampWattageRepository(fixture.NewContext()).GetMeanTotalBilledWAsync(enel);

        mean.Should().NotBeNull();
        mean!.Value.Should().BeApproximately(261.5m / 3m, 0.0000001m);
        mean.Value.Should().NotBe(87.1667m);
    }

    [Fact]
    public async Task UpsertAsync_NewKey_Inserts_AndReturnsTheStoredRow()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        var repo = new EFDistribuidoraLampWattageRepository(fixture.Context);
        var row = Row(enel, LampTypeCode.VS, 100, 17m);

        var stored = await repo.UpsertAsync(row);
        await repo.SaveChangesAsync();

        stored.Should().BeEquivalentTo(row);
        (await new EFDistribuidoraLampWattageRepository(fixture.NewContext()).FindAsync(enel, LampTypeCode.VS, 100))
            .Should().BeEquivalentTo(row);
    }

    [Fact]
    public async Task UpsertAsync_ExistingKey_UpdatesBallastLossInPlace_KeepingTheExistingId()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        var original = Row(enel, LampTypeCode.VS, 100, 17m);
        var seedRepo = new EFDistribuidoraLampWattageRepository(fixture.Context);
        await seedRepo.UpsertAsync(original);
        await seedRepo.SaveChangesAsync();

        var repo = new EFDistribuidoraLampWattageRepository(fixture.NewContext());
        var replacement = Row(enel, LampTypeCode.VS, 100, 21.5m); // carries a different, fresh Id
        var stored = await repo.UpsertAsync(replacement);
        await repo.SaveChangesAsync();

        stored.Id.Should().Be(original.Id).And.NotBe(replacement.Id);
        stored.BallastLossW.Should().Be(21.5m);
        var page = await new EFDistribuidoraLampWattageRepository(fixture.NewContext()).GetPageAsync(enel, 1, 10);
        page.TotalCount.Should().Be(1, "the row was updated in place, not duplicated");
        page.Items.Single().Id.Should().Be(original.Id);
        page.Items.Single().BallastLossW.Should().Be(21.5m);
    }

    [Fact]
    public async Task UpsertAsync_SameKeyTwiceBeforeSaving_StagesASingleRow()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        var repo = new EFDistribuidoraLampWattageRepository(fixture.Context);

        var first = await repo.UpsertAsync(Row(enel, LampTypeCode.VS, 100, 17m));
        var second = await repo.UpsertAsync(Row(enel, LampTypeCode.VS, 100, 19m));
        await repo.SaveChangesAsync();

        second.Id.Should().Be(first.Id);
        var page = await new EFDistribuidoraLampWattageRepository(fixture.NewContext()).GetPageAsync(enel, 1, 10);
        page.Items.Should().ContainSingle().Which.BallastLossW.Should().Be(19m);
    }

    [Fact]
    public async Task UpsertAsync_Null_Throws()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFDistribuidoraLampWattageRepository(fixture.Context);

        await repo.Invoking(r => r.UpsertAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UniqueIndex_RejectsADuplicateKeyWrittenBehindTheRepositorysBack()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        await SeedChartAsync(fixture, enel, (LampTypeCode.VS, 100, 17m));

        var act = () => fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "DistribuidoraLampWattages" ("Id", "DistribuidoraId", "Code", "WattageW", "BallastLossW") SELECT {Guid.NewGuid()}, "DistribuidoraId", "Code", "WattageW", '1.0' FROM "DistribuidoraLampWattages" """);

        await act.Should().ThrowAsync<Exception>().WithMessage("*UNIQUE*");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow_AndUnknownIdIsANoOp()
    {
        using var fixture = new SqliteFixture();
        var enel = await SeedDistribuidoraAsync(fixture);
        await SeedChartAsync(fixture, enel, (LampTypeCode.VS, 100, 17m), (LampTypeCode.LD, 60, 0m));
        var repo = new EFDistribuidoraLampWattageRepository(fixture.NewContext());
        var target = (await repo.FindAsync(enel, LampTypeCode.VS, 100))!;

        await repo.DeleteAsync(Guid.NewGuid());
        await repo.DeleteAsync(target.Id);
        await repo.SaveChangesAsync();

        var page = await new EFDistribuidoraLampWattageRepository(fixture.NewContext()).GetPageAsync(enel, 1, 10);
        page.Items.Should().ContainSingle().Which.Code.Should().Be(LampTypeCode.LD);
    }
}
