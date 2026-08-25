namespace GeoAssets.Core.Theming;

/// <summary>
/// The user's theme selection. <see cref="System"/> follows the OS-level
/// <c>prefers-color-scheme</c> media query rather than pinning to one palette.
/// </summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}
