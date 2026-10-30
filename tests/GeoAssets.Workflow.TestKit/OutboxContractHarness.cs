using GeoAssets.Core.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Disposable test harness bundle for <see cref="OutboxStoreContractTests"/> implementations.
/// </summary>
public sealed record OutboxContractHarness(
    IOutboxWriter Writer,
    IOutboxDispatcherStore DispatcherStore,
    IIdempotentInbox Inbox,
    FakeTimeProvider TimeProvider,
    IAsyncDisposable? Scope = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}
