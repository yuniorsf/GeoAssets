using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Orchestrates the application boot phase.
/// On first launch: shows the provider selection modal.
/// On subsequent launches: auto-reconnects from the persisted config.
/// </summary>
public sealed class BootLoaderService : IBootLoader
{
    private const string StorageKey = "geoassets:boot-config";

    private readonly IProviderPool _pool;
    private readonly ProviderPluginRegistry _registry;
    private readonly IStorageService _storage;
    private readonly IServiceProvider _services;
    private readonly ILogger<BootLoaderService> _logger;

    public bool IsBootComplete { get; private set; }
    public event EventHandler? BootCompleted;

    public BootLoaderService(
        IProviderPool pool,
        ProviderPluginRegistry registry,
        IStorageService storage,
        IServiceProvider services,
        ILogger<BootLoaderService> logger)
    {
        _pool     = pool;
        _registry = registry;
        _storage  = storage;
        _services = services;
        _logger   = logger;
    }

    public async Task<bool> TryAutoBootAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _storage.GetStringAsync(StorageKey, ct);
            if (string.IsNullOrWhiteSpace(json)) return false;

            var saved = JsonSerializer.Deserialize<PersistedBootConfig>(json);
            if (saved is null || string.IsNullOrEmpty(saved.PluginId)) return false;

            var plugin = _registry.Find(saved.PluginId);
            if (plugin is null)
            {
                _logger.LogWarning("Boot config references unknown plugin '{Id}' — showing selector", saved.PluginId);
                return false;
            }

            var config = new ProviderConfig(saved.Values ?? []);
            await BootWithAsync(plugin, config, ct);
            _logger.LogInformation("Auto-boot completed — plugin: {Plugin}", plugin.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-boot failed — clearing persisted config");
            await _storage.SetStringAsync(StorageKey, string.Empty, ct);
            return false;
        }
    }

    public async Task BootWithAsync(IProviderPlugin plugin, ProviderConfig config, CancellationToken ct = default)
    {
        var provider = await plugin.CreateAsync(config, _services, ct);
        var name     = config.Get("name", plugin.DisplayName);
        var entry    = _pool.Add(name, provider);

        _pool.SetActive(entry.Id);

        // Persist — strip file content (potentially large) before saving.
        var toSave = new Dictionary<string, string>(
            config.All.Where(kv => !kv.Key.EndsWith("_content", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var persisted = new PersistedBootConfig(plugin.Id, toSave);
        await _storage.SetStringAsync(StorageKey, JsonSerializer.Serialize(persisted), ct);

        Complete();
    }

    private void Complete()
    {
        IsBootComplete = true;
        BootCompleted?.Invoke(this, EventArgs.Empty);
    }

    // ── Persisted shape ───────────────────────────────────────────────────────

    private sealed record PersistedBootConfig(
        string PluginId,
        Dictionary<string, string> Values);
}
