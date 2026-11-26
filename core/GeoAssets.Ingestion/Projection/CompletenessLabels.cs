using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Projection;

/// <summary>The <c>import.completeness</c> values.</summary>
internal static class CompletenessLabels
{
    public static string For(Completeness completeness) => completeness switch
    {
        Completeness.GeometryOnly => "geometry-only",
        Completeness.Recovered => "recovered",
        _ => "full",
    };

    /// <summary>A geometry-only element that resolved at least one reference by snap or anchor is <see cref="Completeness.Recovered"/>.</summary>
    public static Completeness Effective(Completeness declared, IReadOnlyList<ResolvedReference> resolved)
    {
        if (declared != Completeness.GeometryOnly)
        {
            return declared;
        }

        foreach (var reference in resolved)
        {
            if (reference.Method is ResolutionMethod.Snap or ResolutionMethod.Anchor)
            {
                return Completeness.Recovered;
            }
        }

        return declared;
    }
}
