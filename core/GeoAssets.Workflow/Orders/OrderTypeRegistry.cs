namespace GeoAssets.Workflow.Orders;

/// <summary>
/// In-memory catalogue of known <see cref="OrderType"/> definitions.
///
/// Populate at startup:
/// <code>
///   registry.Register(new OrderType
///   {
///       Id          = "inspection",
///       DisplayName = "Inspección de campo",
///       CreationPolicies = [ new(PolicyKind.Role, "FieldTechnician"),
///                            new(PolicyKind.Role, "Supervisor") ],
///   });
/// </code>
/// </summary>
public sealed class OrderTypeRegistry
{
    private readonly Dictionary<string, OrderType> _types = new(StringComparer.OrdinalIgnoreCase);

    public void Register(OrderType orderType)
        => _types[orderType.Id] = orderType;

    public OrderType? Find(string id)
        => _types.GetValueOrDefault(id);

    public OrderType Get(string id)
        => _types.TryGetValue(id, out var t) ? t
           : throw new KeyNotFoundException($"OrderType '{id}' is not registered.");

    public IReadOnlyCollection<OrderType> All => _types.Values;

    /// <summary>
    /// Loads all order types from <paramref name="repository"/> into the registry,
    /// replacing any existing entry with the same ID.
    ///
    /// Call at startup after the DI container is built:
    /// <code>
    ///   await registry.LoadFromAsync(sp.GetRequiredService&lt;IOrderTypeRepository&gt;());
    /// </code>
    /// </summary>
    public async Task LoadFromAsync(IOrderTypeRepository repository, CancellationToken ct = default)
    {
        var types = await repository.GetAllAsync(ct);
        foreach (var t in types)
            _types[t.Id] = t;
    }
}
