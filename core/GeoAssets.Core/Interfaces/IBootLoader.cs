using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Orchestrates the application boot phase: tries to auto-connect from a
/// persisted config, or waits for the user to pick a provider in the UI.
/// </summary>
public interface IBootLoader
{
    /// <summary>True once a provider has been successfully connected.</summary>
    bool IsBootComplete { get; }

    /// <summary>Fired on the UI thread when boot completes.</summary>
    event EventHandler? BootCompleted;

    /// <summary>
    /// Attempts to restore the last-used provider from persisted config.
    /// Returns <c>true</c> if auto-boot succeeded (modal is not needed).
    /// </summary>
    Task<bool> TryAutoBootAsync(CancellationToken ct = default);

    /// <summary>
    /// Initialises the selected plugin with the given config, adds the resulting
    /// provider to the pool and persists the config for next launch.
    /// </summary>
    Task BootWithAsync(IProviderPlugin plugin, ProviderConfig config, CancellationToken ct = default);
}
