using System.Globalization;
using GeoAssets.Core.Localization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Localization;

/// <summary>
/// Blazor WASM implementation of <see cref="ICultureService"/>.
/// <list type="bullet">
///   <item>Reads the stored preference from <c>localStorage</c> on initialisation.</item>
///   <item>Falls back to <c>navigator.language</c>, then <see cref="LocalizationOptions.DefaultCulture"/>.</item>
///   <item>Persists the chosen culture to <c>localStorage</c> so it survives page reloads.</item>
/// </list>
/// </summary>
public sealed class BlazorCultureService : ICultureService
{
    private const string StorageKey = "geoassets.culture";

    private readonly IJSRuntime _js;
    private readonly LocalizationOptions _options;

    public event EventHandler? CultureChanged;

    public CultureInfo CurrentCulture { get; private set; }

    public IReadOnlyList<CultureInfo> SupportedCultures { get; }

    public BlazorCultureService(IJSRuntime js, IOptions<LocalizationOptions> options)
    {
        _js      = js;
        _options = options.Value;

        SupportedCultures = _options.SupportedCultures
            .Select(c => new CultureInfo(c))
            .ToList();

        // Use default synchronously; InitAsync will override from storage/browser.
        CurrentCulture = new CultureInfo(_options.DefaultCulture);
    }

    /// <summary>
    /// Must be awaited once on app startup (e.g. in <c>App.razor OnInitializedAsync</c>)
    /// to resolve the culture from localStorage / browser before first render.
    /// </summary>
    public async Task InitAsync()
    {
        var stored  = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        var browser = await _js.InvokeAsync<string>("eval", "navigator.language || 'en'");

        var preferred = stored ?? browser.Split('-')[0];   // "en-US" → "en"
        var resolved  = Resolve(preferred);

        await ApplyAsync(resolved, persist: stored is null);
    }

    public async Task SetCultureAsync(string cultureName)
    {
        var culture = Resolve(cultureName);
        await ApplyAsync(culture, persist: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private CultureInfo Resolve(string name)
    {
        // Exact match first, then prefix match ("pt-BR" → "pt"), then default.
        return SupportedCultures.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? SupportedCultures.FirstOrDefault(c => name.StartsWith(c.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            ?? new CultureInfo(_options.DefaultCulture);
    }

    private async Task ApplyAsync(CultureInfo culture, bool persist)
    {
        CurrentCulture = culture;
        CultureInfo.CurrentCulture   = culture;
        CultureInfo.CurrentUICulture = culture;

        if (persist)
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, culture.TwoLetterISOLanguageName);

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
