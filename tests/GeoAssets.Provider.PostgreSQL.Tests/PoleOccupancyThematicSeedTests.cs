using System.Collections;
using System.Reflection;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Provider.PostgreSQL.Data;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-375: the provider references no vertical, so the pole occupancy thematic seed inlines the
/// NeutralOperator vertical's occupancy attribute key and class values. This pins them to the definitions.
/// </summary>
public sealed class PoleOccupancyThematicSeedTests
{
    private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    private static Type SeedType =>
        typeof(GeoAssetsDbContext).Assembly.GetType("GeoAssets.Provider.PostgreSQL.Data.PoleOccupancyThematicSeed", throwOnError: true)!;

    [Fact]
    public void PoleOccupancyThematicSeed_ClassesAndAttribute_MatchTheVerticalsDefinitions()
    {
        var attribute = (string?)SeedType.GetField("OccupancyClassAttribute", NonPublicStatic)!.GetValue(null);
        var themes = (IEnumerable)SeedType.GetProperty("Themes", NonPublicStatic)!.GetValue(null)!;
        var seededClasses = themes.Cast<object>()
            .Select(theme => (string)theme.GetType().GetProperty("Class")!.GetValue(theme)!)
            .ToList();

        attribute.Should().Be(OccupancyAssetAttributes.ClassKey);
        seededClasses.Should().BeEquivalentTo(Enum.GetValues<OccupancyClass>().Select(OccupancyAssetAttributes.ClassValue));
    }
}
