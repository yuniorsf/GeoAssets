namespace GeoAssets.Ingestion.Pipeline;

/// <summary>The feature ids seen (mapped) and projected so far in pass 2, kept as compact <see cref="Guid"/>s.</summary>
internal sealed class ElementRegistry
{
    private readonly HashSet<Guid> _seen = [];
    private readonly HashSet<Guid> _projected = [];

    /// <summary>False when the id was already seen in this run (a duplicate element).</summary>
    public bool TryMarkSeen(Guid featureId) => _seen.Add(featureId);

    public void MarkProjected(Guid featureId) => _projected.Add(featureId);

    public bool IsProjected(Guid featureId) => _projected.Contains(featureId);
}
