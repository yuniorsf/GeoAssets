namespace GeoAssets.Core.Theming;

/// <summary>
/// Manages the active UI theme (dark / light / system) and applies it via
/// <c>data-bs-theme</c> on the document root. Changing the mode persists the
/// preference and raises <see cref="ThemeChanged"/>.
/// </summary>
public interface IThemeService
{
    /// <summary>The user's stored selection — may be <see cref="ThemeMode.System"/>.</summary>
    ThemeMode Mode { get; }

    /// <summary>The actually-applied palette: <c>"dark"</c> or <c>"light"</c>.</summary>
    string ResolvedTheme { get; }

    /// <summary>
    /// Switches to <paramref name="mode"/>, persists the preference, and raises
    /// <see cref="ThemeChanged"/>.
    /// </summary>
    Task SetModeAsync(ThemeMode mode);

    /// <summary>Raised after the resolved theme has changed and been applied to the DOM.</summary>
    event EventHandler ThemeChanged;
}
