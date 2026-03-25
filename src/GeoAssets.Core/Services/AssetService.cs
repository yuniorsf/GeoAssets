using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// Orchestrates the repository and storage: loads on startup, auto-saves on mutation
/// with 500ms debounce to avoid excessive I/O.
/// </summary>
public sealed class AssetService : IAsyncDisposable
{
    private readonly IAssetRepository _repository;
    private readonly IStorageService _storage;
    private CancellationTokenSource _saveCts = new();
    private string _collectionName = "Mis Activos GIS";

    public AssetService(IAssetRepository repository, IStorageService storage)
    {
        _repository = repository;
        _storage = storage;
        _repository.CollectionChanged += OnCollectionChanged;
    }

    public string CollectionName
    {
        get => _collectionName;
        set => _collectionName = value;
    }

    public async Task InitializeAsync()
    {
        var collection = await _storage.LoadAsync();
        _repository.LoadAll(collection.Features);
        _collectionName = collection.Metadata.Name;

        // Merge custom asset types
        foreach (var type in collection.Metadata.AssetTypes.Where(t => !t.IsBuiltIn))
            _repository.AddAssetType(type);
    }

    public async Task<string> ExportAsync()
    {
        var collection = BuildCollection();
        return await _storage.ExportToStringAsync(collection);
    }

    public async Task ImportAsync(string geoJson)
    {
        var imported = await _storage.ImportFromStringAsync(geoJson);

        // Merge asset types
        foreach (var t in imported.Metadata.AssetTypes.Where(t => !t.IsBuiltIn))
            _repository.AddAssetType(t);

        // Merge features (add or update by id)
        foreach (var feature in imported.Features)
        {
            if (_repository.GetById(feature.Id) is not null)
                _repository.Update(feature);
            else
                _repository.Add(feature);
        }
    }

    /// <summary>
    /// Removes all features from the repository and immediately persists
    /// the empty state so the map stays blank on next load.
    /// </summary>
    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        _repository.Clear();
        await _storage.SaveAsync(BuildCollection(), "default", ct);
    }

    private void OnCollectionChanged(object? sender, EventArgs e)
    {
        // Debounce: cancel previous pending save and start a new one
        _saveCts.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                var collection = BuildCollection();
                await _storage.SaveAsync(collection, "default", token);
            }
            catch (OperationCanceledException) { /* debounced */ }
        }, token);
    }

    private GeoFeatureCollection BuildCollection() => new()
    {
        Features = [.. _repository.GetAll()],
        Metadata = new()
        {
            Name = _collectionName,
            AssetTypes = [.. _repository.GetAssetTypes()]
        }
    };

    public async ValueTask DisposeAsync()
    {
        _repository.CollectionChanged -= OnCollectionChanged;
        await _saveCts.CancelAsync();
        _saveCts.Dispose();
    }
}
