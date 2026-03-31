using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Contract that every spatial-data provider plugin must implement.
/// Register implementations in DI as <c>IProviderPlugin</c>; the
/// <see cref="ProviderPluginRegistry"/> collects them for the boot dialog
/// and the pool panel's connect flow.
/// </summary>
public interface IProviderPlugin
{
    /// <summary>Stable slug used for persistence (e.g. "rest", "inmemory").</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the selection list.</summary>
    string DisplayName { get; }

    /// <summary>One-line description shown below the display name.</summary>
    string Description { get; }

    /// <summary>
    /// Declarative schema for the configuration form.
    /// The generic <c>ProviderConfigForm</c> Razor component renders these fields
    /// automatically — no Blazor reference needed in provider projects.
    /// </summary>
    IReadOnlyList<ProviderConfigField> ConfigFields { get; }

    /// <summary>
    /// Creates and fully initialises a provider from the collected config values.
    /// Implementations should be robust: validate required fields and throw
    /// <see cref="InvalidOperationException"/> with a user-friendly message on failure.
    /// </summary>
    Task<IAssetProvider> CreateAsync(
        ProviderConfig config,
        IServiceProvider services,
        CancellationToken ct = default);
}
