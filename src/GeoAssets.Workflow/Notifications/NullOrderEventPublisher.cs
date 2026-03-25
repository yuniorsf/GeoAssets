namespace GeoAssets.Workflow.Notifications;

/// <summary>
/// No-op implementation of <see cref="IOrderEventPublisher"/>.
///
/// Register this when you want to disable notifications without removing
/// any call-sites (unit tests, WASM hosts, development environments).
///
/// <code>
///   services.AddSingleton&lt;IOrderEventPublisher, NullOrderEventPublisher&gt;();
/// </code>
/// </summary>
public sealed class NullOrderEventPublisher : IOrderEventPublisher
{
    public Task PublishAsync(OrderStateChangedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
}
