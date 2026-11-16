namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>Adapts a synchronously disposable test resource to the <see cref="IAsyncDisposable"/> a contract harness owns.</summary>
internal sealed class DisposableScope(IDisposable resource) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        resource.Dispose();
        return ValueTask.CompletedTask;
    }
}
