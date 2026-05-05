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

public enum ProviderFieldType
{
    Text,
    Password,
    Url,
    Number,
    Checkbox,
    /// <summary>
    /// Renders an <c>InputFile</c> picker. After the user picks a file the
    /// component stores the raw text content under the key suffixed with
    /// <c>_content</c> (e.g. key "file" → config["file_content"]).
    /// </summary>
    File,
    Select,
    /// <summary>
    /// Renders an <c>InputFile</c> picker for binary files (e.g. Shapefile ZIP).
    /// Stores the raw bytes as a Base-64 string under <c>key_content</c> and
    /// the file name under <c>key_name</c>.
    /// </summary>
    BinaryFile
}
