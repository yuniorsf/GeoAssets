using GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;
using GeoAssets.Ingestion.Profiles.NeoenergiaGse;

namespace GeoAssets.Ingestion.Profiles;

/// <summary>The source profiles shipped with GeoAssets.</summary>
public static class BuiltInProfiles
{
    public static ISourceProfileRegistry CreateRegistry() =>
        new SourceProfileRegistry([new NeoenergiaGseProfile(), new EquatorialQgisMunicipalProfile()]);
}
