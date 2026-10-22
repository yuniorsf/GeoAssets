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
}
