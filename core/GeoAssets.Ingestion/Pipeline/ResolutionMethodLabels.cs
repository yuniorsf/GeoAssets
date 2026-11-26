using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>Stable lower-case labels of <see cref="ResolutionMethod"/> (report keys and edge metadata).</summary>
internal static class ResolutionMethodLabels
{
    public const string Key = "key";
    public const string Snap = "snap";
    public const string Anchor = "anchor";

    public static string For(ResolutionMethod method) => method switch
    {
        ResolutionMethod.Snap => Snap,
        ResolutionMethod.Anchor => Anchor,
        _ => Key,
    };
}
