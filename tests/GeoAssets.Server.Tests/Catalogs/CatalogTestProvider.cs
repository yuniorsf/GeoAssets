namespace GeoAssets.Server.Tests.Catalogs;

/// <summary>An in-memory catalog that offers <see cref="ITestCatalogCapability"/> and counts how often it is disposed.</summary>
internal sealed class CatalogTestProvider(string origin) : TestAssetProvider, ITestCatalogCapability, IAsyncDisposable
{
    private int _disposeCount;

    public string Origin { get; } = origin;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return ValueTask.CompletedTask;
    }
}
