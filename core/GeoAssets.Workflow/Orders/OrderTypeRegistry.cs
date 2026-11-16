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

    /// <summary>
    /// Adds <paramref name="orderType"/>, replacing any existing entry with the same ID.
    /// </summary>
    /// <remarks>
    /// When the type carries a <see cref="OrderType.TargetPolicy"/>, the policy is validated
    /// first (<see cref="Targets.OrderTargetPolicy.Validate"/>) and an invalid one is rejected
    /// without touching the registry. That validation covers only what the type can prove about
    /// itself. Whether a fan-out policy's child order type is registered is deliberately NOT
    /// checked here: registration order is not guaranteed (hosts register a parent type before
    /// its child, and <see cref="LoadFromAsync"/> overlays types in repository order), so the
    /// child is resolved with <see cref="Find"/> when the fan-out actually runs.
    /// </remarks>
    /// <exception cref="ArgumentException">The type's target policy is invalid.</exception>
    public void Register(OrderType orderType)
    {
        ArgumentNullException.ThrowIfNull(orderType);
        orderType.TargetPolicy?.Validate(orderType.Id);
        _types[orderType.Id] = orderType;
    }

    public OrderType? Find(string id)
        => _types.GetValueOrDefault(id);

    public OrderType Get(string id)
        => _types.TryGetValue(id, out var t) ? t
           : throw new KeyNotFoundException($"OrderType '{id}' is not registered.");

    public IReadOnlyCollection<OrderType> All => _types.Values;

    /// <summary>
    /// Loads all order types from <paramref name="repository"/> into the registry,
    /// replacing any existing entry with the same ID. Every loaded type's target policy is
    /// validated exactly as <see cref="Register"/> does; if any is invalid the call throws
    /// and the registry is left as it was.
    ///
    /// Call at startup after the DI container is built:
    /// <code>
    ///   await registry.LoadFromAsync(sp.GetRequiredService&lt;IOrderTypeRepository&gt;());
    /// </code>
    /// </summary>
    public async Task LoadFromAsync(IOrderTypeRepository repository, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var types = await repository.GetAllAsync(ct).ConfigureAwait(false);
        foreach (var t in types)
            t.TargetPolicy?.Validate(t.Id);
        foreach (var t in types)
            _types[t.Id] = t;
    }
}
