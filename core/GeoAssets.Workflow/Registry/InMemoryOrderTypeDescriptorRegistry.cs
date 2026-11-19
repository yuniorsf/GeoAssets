namespace GeoAssets.Workflow.Registry;

/// <summary>
/// The in-process <see cref="IOrderTypeDescriptorRegistry"/> (XD01-369): held in memory, so a remote vertical republishes
/// its set when it starts (its <c>PUT</c> is idempotent); the in-process modules' sets (<see cref="OrderTypeDescriptorSource"/>)
/// are upserted when the registry is created. Thread-safe.
/// </summary>
public sealed class InMemoryOrderTypeDescriptorRegistry : IOrderTypeDescriptorRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<OrderTypeDescriptor>> _verticals = new(StringComparer.Ordinal);

    public InMemoryOrderTypeDescriptorRegistry(IEnumerable<OrderTypeDescriptorSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        foreach (var source in sources)
            Upsert(source.VerticalId, [.. source.Descriptors]);
    }

    public bool Upsert(string verticalId, IReadOnlyCollection<OrderTypeDescriptor> descriptors)
    {
        if (OrderTypeDescriptorValidation.FindProblem(verticalId, descriptors) is { } problem)
            throw new ArgumentException(problem, nameof(descriptors));

        List<OrderTypeDescriptor> incoming = [.. descriptors.OrderBy(d => d.OrderTypeId, StringComparer.Ordinal)];
        lock (_gate)
        {
            var current = _verticals.GetValueOrDefault(verticalId) ?? [];
            if (current.Count == incoming.Count && current.Zip(incoming).All(pair => pair.First.SameAs(pair.Second)))
                return false;

            if (incoming.Count == 0) _verticals.Remove(verticalId);
            else _verticals[verticalId] = incoming;
            return true;
        }
    }

    public IReadOnlyList<OrderTypeDescriptor> GetAll()
    {
        lock (_gate)
        {
            return [.. _verticals.OrderBy(v => v.Key, StringComparer.Ordinal).SelectMany(v => v.Value)];
        }
    }

    public IReadOnlyList<FeatureActionDescriptor> FeatureActionsFor(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var wanted = roles.ToHashSet(StringComparer.Ordinal);
        return [.. GetAll().SelectMany(d => d.FeatureActions).Where(a => wanted.Contains(a.Role))];
    }
}
