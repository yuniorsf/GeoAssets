namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Optional extension of <see cref="IExternalProviderFactory"/> for factories
/// that require async initialization (e.g. loading initial data over a network).
/// <see cref="ProviderPoolPanel"/> checks for this interface and calls
/// <see cref="CreateAsync"/> instead of wrapping <see cref="IExternalProviderFactory.Create"/>
/// in <c>Task.Run</c>.
/// </summary>
public interface IAsyncProviderFactory : IExternalProviderFactory
{
    /// <summary>
    /// Creates and fully initializes an <see cref="IAssetProvider"/> asynchronously.
    /// </summary>
    Task<IAssetProvider> CreateAsync(string connectionString, CancellationToken ct = default);
}
