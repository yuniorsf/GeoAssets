namespace GeoAssets.Ingestion.Model;

/// <summary>How a reference was resolved (stamped on the edge as metadata <c>resolution</c>).</summary>
public enum ResolutionMethod
{
    Key,
    Snap,

    /// <summary>A group-anchor reference (<see cref="ElementReference.GroupKey"/>) resolved to its group's single anchor.</summary>
    Anchor,
}
