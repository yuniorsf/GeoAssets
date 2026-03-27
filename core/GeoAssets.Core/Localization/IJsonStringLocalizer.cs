namespace GeoAssets.Core.Localization;

/// <summary>
/// Retrieves localized strings from a loaded JSON translation file.
///
/// <para>
/// Platform-agnostic: the same interface is used by Blazor WASM (HTTP loader),
/// MAUI (bundle file loader), and any future host. Only the concrete
/// implementation changes — the JSON files themselves are identical across platforms.
/// </para>
/// </summary>
public interface IJsonStringLocalizer
{
    /// <summary>Returns the translation for <paramref name="key"/>, or the key itself when not found.</summary>
    string this[string key] { get; }

    /// <summary>Returns the translation with <see cref="string.Format(string,object[])"/> substitution.</summary>
    string GetString(string key, params object[] args);

    /// <summary>Raised after a new language has been loaded and is ready to use.</summary>
    event EventHandler LocalizationChanged;
}
