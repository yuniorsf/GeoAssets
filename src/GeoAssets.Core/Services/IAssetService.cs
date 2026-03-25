namespace GeoAssets.Core.Services;

/// <summary>
/// Application-level facade over <see cref="IAssetRepository"/> and
/// <see cref="GeoAssets.Core.Interfaces.IStorageService"/>.
///
/// Concrete implementation: <see cref="AssetService"/>.
/// Observable decorator:    <c>ObservableAssetService</c> (GeoAssets.Shared).
/// </summary>
public interface IAssetService : IAsyncDisposable
{
    string CollectionName { get; set; }

    /// <summary>Loads the persisted collection into the repository on startup.</summary>
    Task InitializeAsync();

    /// <summary>Serialises the current repository state to a GeoJSON string.</summary>
    Task<string> ExportAsync();

    /// <summary>Parses <paramref name="geoJson"/> and merges features into the repository.</summary>
    Task ImportAsync(string geoJson);

    /// <summary>Removes all features and persists the empty state.</summary>
    Task ClearAllAsync(CancellationToken ct = default);
}
