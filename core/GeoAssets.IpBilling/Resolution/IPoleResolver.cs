namespace GeoAssets.IpBilling.Resolution;

/// <summary>
/// Resolves the "Poste (IP)" <c>GeoFeature</c>(s) fed by a given transformer (the
/// <c>TopoEdge(Kind="feeds")</c> Transformer→Pole edges, mirroring
/// <c>examples/Topology/01_ElectricCircuit.cs</c>). Concrete implementation lands with
/// Phase 1 (XD01-179); this ticket only adds the interface.
/// </summary>
public interface IPoleResolver
{
    /// <summary>Returns the <c>GeoFeature.Id</c>s of every pole fed by the given transformer feature.</summary>
    Task<IReadOnlyList<Guid>> ResolvePoleFeatureIdsAsync(
        Guid transformerFeatureId, CancellationToken ct = default);

    /// <summary>
    /// Batch-resolves the fed pole <c>GeoFeature.Id</c>s for multiple transformer features in a
    /// single operation (used during municipal spreadsheet calculation runs).
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ResolvePoleFeatureIdsBatchAsync(
        IReadOnlyList<Guid> transformerFeatureIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transformerFeatureIds);
        var result = new Dictionary<Guid, IReadOnlyList<Guid>>(transformerFeatureIds.Count);
        foreach (var id in transformerFeatureIds)
            result[id] = await ResolvePoleFeatureIdsAsync(id, ct).ConfigureAwait(false);
        return result;
    }
}
