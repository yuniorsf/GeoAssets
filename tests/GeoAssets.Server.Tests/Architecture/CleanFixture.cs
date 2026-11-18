using GeoAssets.Core.Models;

namespace GeoAssets.Server.Tests.Architecture.Fixtures;

internal sealed class CleanFixture
{
    public static string Describe(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Id ?? string.Empty;
    }
}
