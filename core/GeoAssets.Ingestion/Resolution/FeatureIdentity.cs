using System.Runtime.InteropServices;
using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>The deterministic feature id of an element key, as text and as a compact <see cref="Guid"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FeatureIdentity(string Text, Guid Value)
{
    public static FeatureIdentity For(SourceKey key) => FromText(DeterministicFeatureId.For(key));

    public static FeatureIdentity FromText(string text) => new(text, Guid.ParseExact(text, "D"));
}
