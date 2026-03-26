namespace GeoAssets.Core.Localization;

/// <summary>
/// Configuration for the JSON localization system.
/// Bound from <c>appsettings.json → Localization</c>.
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>Culture used when no preference is stored and browser detection fails.</summary>
    public string DefaultCulture { get; set; } = "es";

    /// <summary>
    /// All cultures that have a corresponding JSON file.
    /// Each entry must match a file at <c>{ResourcePath}/{culture}.json</c>.
    /// </summary>
    public List<string> SupportedCultures { get; set; } = ["es", "en", "pt"];

    /// <summary>
    /// Base URL (Blazor WASM) or folder path (MAUI) of the i18n JSON files.
    /// Blazor default: <c>_content/GeoAssets.Shared/i18n</c>
    /// MAUI default:   <c>i18n</c>  (relative to app bundle resources)
    /// </summary>
    public string ResourcePath { get; set; } = "_content/GeoAssets.Shared/i18n";
}
