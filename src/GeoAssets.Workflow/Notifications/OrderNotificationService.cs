namespace GeoAssets.Workflow.Notifications;

/// <summary>
/// Default implementation of <see cref="IOrderNotificationService"/>.
///
/// Builds a single <see cref="OrderStateChangedEvent"/> enriched with the
/// resolved recipient list (stored in <see cref="OrderStateChangedEvent.Metadata"/>
/// under the key <c>"recipients"</c>) and publishes it via the configured
/// <see cref="IOrderEventPublisher"/>.
///
/// Consumer systems (Service Bus, Kafka, …) are responsible for fan-out to
/// individual actors.
/// </summary>
///
/// <example>
/// <b>1. Inject and call after a status transition</b>
/// <code>
/// public sealed class ServiceOrderCommandHandler(
///     IServiceOrderRepository   orders,
///     IOrderNotificationService notifications)
/// {
///     public async Task TransitionAsync(
///         string orderId, ServiceOrderStatus newStatus,
///         string performedBy, CancellationToken ct = default)
///     {
///         var order = await orders.GetByIdAsync(orderId, ct)
///                     ?? throw new KeyNotFoundException(orderId);
///
///         var previous = order.Status;
///         // ... mutate and save order ...
///
///         // Resolve the actors who should be notified.
///         var recipients = new List&lt;string&gt; { order.CreatedBy };
///         if (order.AssignedTo is not null) recipients.Add(order.AssignedTo);
///         foreach (var d in order.Dispatches) recipients.Add(d.TargetId);
///
///         await notifications.NotifyStateChangedAsync(
///             orderId      : order.Id,
///             orderTypeId  : order.OrderTypeId,
///             previous     : previous,
///             newStatus    : newStatus,
///             performedBy  : performedBy,
///             recipientIds : recipients,
///             ct           : ct);
///     }
/// }
/// </code>
///
/// <b>2. Register — no-op (WASM / unit tests)</b>
/// <code>
/// // Program.cs  (Blazor WASM)
/// builder.Services.AddWorkflowNotifications();
/// </code>
///
/// <b>3. Register — Azure Service Bus</b>
/// <code>
/// // Program.cs  (ASP.NET Core host)
/// builder.Services.AddWorkflowServiceBus(builder.Configuration);
/// // appsettings.json:
/// // "WorkflowServiceBus": {
/// //   "ConnectionString": "Endpoint=sb://my-ns.servicebus.windows.net/;...",
/// //   "TopicName": "order-state-changed"
/// // }
/// </code>
///
/// <b>4. Register — Apache Kafka</b>
/// <code>
/// // Program.cs  (ASP.NET Core host)
/// builder.Services.AddWorkflowKafka(opts =>
/// {
///     opts.BootstrapServers = "broker1:9092,broker2:9092";
///     opts.TopicName        = "order-state-changed";
///     opts.ClientId         = "geoassets-api";
/// });
/// </code>
///
/// <b>5. Swap the transport without touching call-sites</b>
/// <code>
/// // Replace the publisher only — keep OrderNotificationService as-is.
/// builder.Services.AddWorkflowNotifications&lt;MyCustomPublisher&gt;();
/// </code>
/// </example>
public sealed class OrderNotificationService(IOrderEventPublisher publisher)
    : IOrderNotificationService
{
    public async Task NotifyStateChangedAsync(
        string                               orderId,
        string                               orderTypeId,
        Orders.ServiceOrderStatus            previous,
        Orders.ServiceOrderStatus            newStatus,
        string                               performedBy,
        IEnumerable<string>                  recipientIds,
        string?                              correlationId = null,
        IReadOnlyDictionary<string, string>? metadata      = null,
        CancellationToken                    ct            = default)
    {
        var recipients = string.Join(",", recipientIds);

        var enriched = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>())
        {
            ["recipients"] = recipients,
        };

        var evt = OrderStateChangedEvent.Create(
            orderId,
            orderTypeId,
            previous,
            newStatus,
            performedBy,
            correlationId,
            enriched);

        await publisher.PublishAsync(evt, ct).ConfigureAwait(false);
    }
}
