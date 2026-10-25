namespace GeoAssets.Core.Models;

/// <summary>
/// Describes one input field in a provider's configuration form.
/// The generic <c>ProviderConfigForm</c> component renders the appropriate
/// HTML element based on <see cref="Type"/>.
/// </summary>
public sealed record ProviderConfigField(
    string Key,
    string Label,
    ProviderFieldType Type = ProviderFieldType.Text,
    string Placeholder = "",
    bool Required = false,
    string? DefaultValue = null,
    /// <summary>Options list — only used when <see cref="Type"/> is <see cref="ProviderFieldType.Select"/>.</summary>
    IReadOnlyList<string>? Options = null,
    /// <summary>
    /// File accept filter — only used when <see cref="Type"/> is <see cref="ProviderFieldType.File"/>
    /// or <see cref="ProviderFieldType.BinaryFile"/>.
    /// Defaults to <c>.geojson,.json</c> for <see cref="ProviderFieldType.File"/>
    /// and <c>.zip</c> for <see cref="ProviderFieldType.BinaryFile"/>.
    /// </summary>
    string? Accept = null);
