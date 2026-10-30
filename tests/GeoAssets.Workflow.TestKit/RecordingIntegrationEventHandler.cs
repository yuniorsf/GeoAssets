using System.Diagnostics;
using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.TestKit;

public sealed class RecordingIntegrationEventHandler<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : notnull
{
    private readonly List<(TEvent Message, IntegrationMessageContext Context, ActivityContext? CurrentActivityContext)> _invocations = [];

    public IReadOnlyList<(TEvent Message, IntegrationMessageContext Context, ActivityContext? CurrentActivityContext)> Invocations => _invocations;

    public Func<TEvent, IntegrationMessageContext, CancellationToken, Task>? OnHandleAsync { get; set; }

    public async Task HandleAsync(TEvent message, IntegrationMessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();

        if (OnHandleAsync is not null)
        {
            await OnHandleAsync(message, context, ct);
        }

        _invocations.Add((message, context, Activity.Current?.Context));
    }
}
