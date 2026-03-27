using System.Globalization;

namespace GeoAssets.Core.Localization;

/// <summary>
/// Manages the active UI culture and exposes the list of supported cultures.
/// Changing the culture triggers <see cref="CultureChanged"/>, which causes
/// <see cref="IJsonStringLocalizer"/> to reload translations and notify components.
/// </summary>
public interface ICultureService
{
    /// <summary>The currently active culture.</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>All cultures the application has translation files for.</summary>
    IReadOnlyList<CultureInfo> SupportedCultures { get; }

    /// <summary>
    /// Switches to <paramref name="cultureName"/> (e.g. "en", "es", "pt"),
    /// persists the preference, and raises <see cref="CultureChanged"/>.
    /// </summary>
    Task SetCultureAsync(string cultureName);

    /// <summary>Raised after the culture has changed and translations are ready.</summary>
    event EventHandler CultureChanged;
}
