namespace GeoAssets.Workflow.Registry;

/// <summary>The kernel's order-type descriptor registry (XD01-369): each vertical's published descriptors.</summary>
public interface IOrderTypeDescriptorRegistry
{
    /// <summary>
    /// Replaces <paramref name="verticalId"/>'s whole descriptor set with <paramref name="descriptors"/> (an empty set
    /// removes the vertical). Idempotent: the same set again changes nothing.
    /// </summary>
    /// <returns><see langword="true"/> when the registry changed.</returns>
    /// <exception cref="ArgumentException">The set is invalid (<see cref="OrderTypeDescriptorValidation"/>).</exception>
    bool Upsert(string verticalId, IReadOnlyCollection<OrderTypeDescriptor> descriptors);

    /// <summary>Every published descriptor, by vertical id then order type id (ordinal).</summary>
    IReadOnlyList<OrderTypeDescriptor> GetAll();

    /// <summary>The feature actions of every vertical whose role is one of <paramref name="roles"/> (ordinal).</summary>
    IReadOnlyList<FeatureActionDescriptor> FeatureActionsFor(IEnumerable<string> roles);
}
