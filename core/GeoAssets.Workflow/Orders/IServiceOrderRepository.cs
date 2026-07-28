namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Full read/write persistence abstraction for <see cref="IServiceOrder"/> — composes
/// <see cref="IServiceOrderReader"/> and <see cref="IServiceOrderWriter"/>. Swap
/// implementations (in-memory, EF Core, remote API) via DI. A consumer that only needs
/// one side can depend on <see cref="IServiceOrderReader"/> or <see cref="IServiceOrderWriter"/>
/// directly instead of this composite.
/// </summary>
public interface IServiceOrderRepository : IServiceOrderReader, IServiceOrderWriter;
