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
    /// File accept filter, in the HTML <c>accept</c> attribute's comma-separated form
    /// (e.g. <c>.zip</c> or <c>.pmtiles,.mbtiles</c>) — only used when <see cref="Type"/> is
    /// <see cref="ProviderFieldType.File"/> or <see cref="ProviderFieldType.BinaryFile"/>.
    /// Defaults to <c>.geojson,.json</c> for <see cref="ProviderFieldType.File"/>; a
    /// <see cref="ProviderFieldType.BinaryFile"/> field without one accepts any file.
    /// </summary>
    string? Accept = null)
{
    /// <summary>
    /// The file extensions <see cref="Accept"/> declares, each with a leading dot and de-duplicated
    /// ignoring case, in declaration order — what a native file picker filters by
    /// (<see cref="Interfaces.INativeFilePicker.PickBinaryFileAsync"/>). MIME-type entries
    /// (<c>application/zip</c>, <c>image/*</c>) are skipped. Returns <c>null</c> when no extension is
    /// declared, meaning any file.
    /// </summary>
    public IReadOnlyList<string>? GetAcceptedExtensions()
    {
        if (string.IsNullOrWhiteSpace(Accept)) return null;

        var extensions = new List<string>();
        foreach (var entry in Accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry.Contains('/', StringComparison.Ordinal)) continue;

            var trimmed = entry.TrimStart('.');
            if (trimmed.Length == 0) continue;

            var extension = "." + trimmed;
            if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) extensions.Add(extension);
        }

        return extensions.Count == 0 ? null : extensions;
    }
}
