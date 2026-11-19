using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Migrations;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The thematic styling of poles by occupancy (XD01-318): the <c>SeedPoleOccupancyThematicLayers</c>
/// migration seeds one layer and one layer rule per <see cref="OccupancyClass"/> on the seeded
/// pole asset type, each matching the <see cref="OccupancyAssetAttributes.ClassKey"/> attribute an
/// approval writes — checked on the operations the migration builds and on the real PostGIS
/// instance the collection fixture migrates, through the provider and <see cref="LayerResolver"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PoleOccupancyThematicLayerTests(PostgresContainerFixture fixture)
{
    private const string ThematicMigration = "SeedPoleOccupancyThematicLayers";

    private static readonly Guid PoleAssetTypeId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid DefaultPoleLayerId = Guid.Parse("00000000-0000-0000-0001-000000000001");

    private sealed class ExposedMigration : SeedPoleOccupancyThematicLayers
    {
        public new void Up(MigrationBuilder migrationBuilder) => base.Up(migrationBuilder);
        public new void Down(MigrationBuilder migrationBuilder) => base.Down(migrationBuilder);
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    [Fact]
    public void Up_InsertsFiveLayersRulesAndConditions_AndDownDeletesExactlyThem()
    {
        var up = new MigrationBuilder(activeProvider: null);
        new ExposedMigration().Up(up);
        var down = new MigrationBuilder(activeProvider: null);
        new ExposedMigration().Down(down);

        var inserts = up.Operations.OfType<InsertDataOperation>().ToList();
        inserts.Select(op => op.Table).Should().Equal("layer", "layer_rule", "layer_rule_condition");
        inserts.Should().OnlyContain(op => op.Values.GetLength(0) == 5);
        up.Operations.Should().AllBeOfType<InsertDataOperation>("the migration only seeds data");
        down.Operations.Should().AllBeOfType<DeleteDataOperation>().And.HaveCount(15);
    }

    [Fact]
    public async Task Migration_AppliedByTheFixture_GivesThePoleTypeOneRulePerOccupancyClass_OnTheClassAttribute()
    {
        await using (var db = new GeoAssetsDbContext(fixture.CreateOptions()))
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith(ThematicMigration, StringComparison.Ordinal));

        await using var sut = CreateSut(fixture.CreateOptions());

        var rules = sut.GetLayerRules(PoleAssetTypeId);

        rules.Should().HaveCount(5);
        rules.Should().OnlyContain(rule => rule.Conditions.Count == 1
            && string.Equals(rule.Conditions[0].Attribute, OccupancyAssetAttributes.ClassKey, StringComparison.Ordinal)
            && rule.Conditions[0].Operator == LayerRuleOperator.Equals);
        rules.Select(rule => rule.Conditions[0].Value).Should().BeEquivalentTo(Enum.GetValues<OccupancyClass>().Select(OccupancyAssetAttributes.ClassValue));
        rules.Select(rule => rule.LayerId).Should().OnlyHaveUniqueItems();
        var layerIds = sut.GetLayers().Select(layer => layer.Id).ToHashSet();
        rules.Should().OnlyContain(rule => layerIds.Contains(rule.LayerId), "every rule points at a seeded layer");
    }

    [Theory]
    [InlineData(OccupancyClass.None)]
    [InlineData(OccupancyClass.Low)]
    [InlineData(OccupancyClass.High)]
    [InlineData(OccupancyClass.OverCapacity)]
    [InlineData(OccupancyClass.Unidentified)]
    public async Task APoleCarryingAnOccupancyClass_ResolvesToThatClassesLayer_AndAPoleWithoutOne_ToThePoleDefault(OccupancyClass occupancyClass)
    {
        await using var sut = CreateSut(fixture.CreateOptions());
        var poleType = sut.GetAssetTypes().Single(type => type.Id == PoleAssetTypeId);
        var layers = sut.GetLayers();
        var rules = sut.GetLayerRules(PoleAssetTypeId);
        var committed = Pole();
        committed.Properties.CustomAttributes[OccupancyAssetAttributes.ClassKey] = OccupancyAssetAttributes.ClassValue(occupancyClass);

        var themed = LayerResolver.Resolve(committed, poleType, layers, rules);
        var plain = LayerResolver.Resolve(Pole(), poleType, layers, rules);

        var expectedLayerId = rules.Single(rule => string.Equals(rule.Conditions[0].Value, OccupancyAssetAttributes.ClassValue(occupancyClass), StringComparison.Ordinal)).LayerId;
        themed!.Id.Should().Be(expectedLayerId);
        themed.Id.Should().NotBe(DefaultPoleLayerId);
        plain!.Id.Should().Be(DefaultPoleLayerId, "a pole whose occupancy was never committed keeps the pole's default style");
    }

    private static GeoFeature Pole() => new()
    {
        Id = "pole-1",
        Properties = new GeoFeatureProperties { Name = "pole-1", AssetTypeId = PoleAssetTypeId.ToString() },
    };
}
