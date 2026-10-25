using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Persistence.Entities;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFLampTypeRepositoryTests
{
    // LampType/LampWattageOption are seeded by an EF Core migration in the real host (see
    // GeoAssets.Server's InitialCreate migration), never written by application code — so
    // these tests seed directly through the DbContext, exactly standing in for "what the
    // migration already put there" rather than going through ILampTypeRepository (which is
    // read-only by design, see its own doc comment).
    private static Task<int> SeedAsync(IpBillingDbContext db)
    {
        db.Set<LampTypeRecord>().Add(new LampTypeRecord
        {
            Id   = Guid.NewGuid(),
            Code = LampTypeCode.VS,
            Name = "Vapor de Sódio",
            WattageOptions =
            [
                new LampWattageOptionRecord { WattageW = 70,  BallastLossW = 8m },
                new LampWattageOptionRecord { WattageW = 100, BallastLossW = 10m },
            ],
        });

        db.Set<LampTypeRecord>().Add(new LampTypeRecord
        {
            Id   = Guid.NewGuid(),
            Code = LampTypeCode.LD,
            Name = "LED",
            WattageOptions =
            [
                new LampWattageOptionRecord { WattageW = 50, BallastLossW = 0m },
            ],
        });

        return db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetByCodeAsync_ExistingCode_ReturnsLampTypeWithWattageOptions()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture.Context);
        var repo = new EFLampTypeRepository(fixture.Context);

        var result = await repo.GetByCodeAsync(LampTypeCode.VS);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Vapor de Sódio");
        result.WattageOptions.Should().HaveCount(2);
        result.WattageOptions.Should().ContainSingle(w => w.WattageW == 70 && w.BallastLossW == 8m);
        result.WattageOptions.Should().ContainSingle(w => w.WattageW == 100 && w.BallastLossW == 10m);
    }

    [Fact]
    public async Task GetByCodeAsync_MissingCode_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture.Context);
        var repo = new EFLampTypeRepository(fixture.Context);

        var result = await repo.GetByCodeAsync(LampTypeCode.MT);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryLampTypeOrderedByCode()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture.Context);
        var repo = new EFLampTypeRepository(fixture.Context);

        var result = await repo.GetAllAsync();

        result.Select(t => t.Code).Should().Equal(LampTypeCode.LD, LampTypeCode.VS);
    }

    [Fact]
    public async Task LampWattageOption_TotalBilledW_SumsWattageAndBallastLoss()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture.Context);
        var repo = new EFLampTypeRepository(fixture.Context);

        var vs = await repo.GetByCodeAsync(LampTypeCode.VS);

        vs!.WattageOptions.Single(w => w.WattageW == 70).TotalBilledW.Should().Be(78m);
    }
}
