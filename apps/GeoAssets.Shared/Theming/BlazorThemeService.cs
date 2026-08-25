using GeoAssets.Core.Theming;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Theming;

/// <summary>
/// Blazor WASM implementation of <see cref="IThemeService"/>.
/// <list type="bullet">
///   <item>Reads the stored preference from <c>localStorage</c> on initialisation.</item>
///   <item>Falls back to <c>prefers-color-scheme</c> when the mode is <see cref="ThemeMode.System"/>
///   or nothing has been stored yet.</item>
///   <item>Applies the resolved theme via <c>data-bs-theme</c> on <c>&lt;html&gt;</c>.</item>
/// </list>
///
/// A tiny inline script in <c>index.html</c> (Web and MAUI) resolves the same preference
/// synchronously before first paint, so there's no flash of the wrong theme during the WASM
/// boot — <see cref="InitAsync"/> here just syncs the C# state (for the topbar's active-button
/// display) to whatever the inline script already applied; both use identical resolution logic
/// so re-applying the attribute is a harmless no-op.
/// </summary>
public sealed class BlazorThemeService : IThemeService
{
    private const string StorageKey = "geoassets.theme";

    private readonly IJSRuntime _js;

    public event EventHandler? ThemeChanged;

    public ThemeMode Mode          { get; private set; } = ThemeMode.System;
    public string    ResolvedTheme { get; private set; } = "dark";

    public BlazorThemeService(IJSRuntime js) => _js = js;

    /// <summary>
    /// Must be awaited once on app startup (e.g. in <c>App.razor OnInitializedAsync</c>)
    /// to resolve the mode from localStorage before first render.
    /// </summary>
    public async Task InitAsync()
    {
        var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        var mode   = ParseMode(stored) ?? ThemeMode.System;

        await ApplyAsync(mode, persist: false);
    }

    public async Task SetModeAsync(ThemeMode mode) => await ApplyAsync(mode, persist: true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ApplyAsync(ThemeMode mode, bool persist)
    {
        var systemPrefersDark = await _js.InvokeAsync<bool>(
            "eval", "window.matchMedia('(prefers-color-scheme: dark)').matches");

        Mode          = mode;
        ResolvedTheme = ResolveTheme(mode, systemPrefersDark);

        await _js.InvokeVoidAsync("document.documentElement.setAttribute", "data-bs-theme", ResolvedTheme);

        if (persist)
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, mode.ToString().ToLowerInvariant());

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resolves a stored mode to the palette that should actually be applied.</summary>
    public static string ResolveTheme(ThemeMode mode, bool systemPrefersDark) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark  => "dark",
        _               => systemPrefersDark ? "dark" : "light",
    };

    /// <summary>Parses a stored localStorage value back into a <see cref="ThemeMode"/>, if valid.</summary>
    public static ThemeMode? ParseMode(string? stored) => stored switch
    {
        "light"  => ThemeMode.Light,
        "dark"   => ThemeMode.Dark,
        "system" => ThemeMode.System,
        _        => null,
    };
}
