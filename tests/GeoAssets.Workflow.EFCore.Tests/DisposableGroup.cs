namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Owns the resources a contract harness opens along the way (extra contexts for other
/// organizations) and disposes them, last opened first, with the harness.
/// </summary>
internal sealed class DisposableGroup : IAsyncDisposable
{
    private readonly Stack<object> _resources = new();

    public T Add<T>(T resource)
        where T : class
    {
        _resources.Push(resource);
        return resource;
    }

    public async ValueTask DisposeAsync()
    {
        while (_resources.TryPop(out var resource))
        {
            if (resource is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (resource is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
