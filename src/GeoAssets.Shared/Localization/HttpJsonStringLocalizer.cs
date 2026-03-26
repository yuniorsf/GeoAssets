using System.Globalization;
using System.Net.Http.Json;
using GeoAssets.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Shared.Localization;

/// <summary>
/// Blazor WASM implementation of <see cref="IJsonStringLocalizer"/>.
/// Loads translation files via <see cref="HttpClient"/> from the static assets path
/// (<c>_content/GeoAssets.Shared/i18n/{culture}.json</c>).
///
/// <para>
/// Loaded dictionaries are cached by culture name so subsequent language
/// switches to an already-visited language are instant.
/// </para>
///
/// <para><b>MAUI reuse:</b> replace this class with a <c>FileJsonStringLocalizer</c>
/// that reads from the app bundle using <c>FileSystem.OpenAppPackageFileAsync</c>.
/// The JSON files, interface, and all components remain unchanged.</para>
/// </summary>
public sealed class HttpJsonStringLocalizer : IJsonStringLocalizer
{
    private readonly HttpClient _http;
    private readonly LocalizationOptions _options;
    private readonly ILogger<HttpJsonStringLocalizer> _logger;

    // Cache: culture name → flat key/value dictionary
    private readonly Dictionary<string, Dictionary<string, string>> _cache = [];

    private Dictionary<string, string> _current = [];

    public event EventHandler? LocalizationChanged;

    public HttpJsonStringLocalizer(
        HttpClient http,
        IOptions<LocalizationOptions> options,
        ICultureService cultureService,
        ILogger<HttpJsonStringLocalizer> logger)
    {
        _http    = http;
        _options = options.Value;
        _logger  = logger;

        cultureService.CultureChanged += OnCultureChanged;
    }

    public string this[string key] => GetString(key);

    public string GetString(string key, params object[] args)
    {
        if (_current.TryGetValue(key, out var value))
            return args.Length > 0 ? string.Format(value, args) : value;

        _logger.LogDebug("Missing translation key: {Key} [{Culture}]",
            key, CultureInfo.CurrentUICulture.Name);
        return key;
    }

    /// <summary>
    /// Loads translations for the given culture. Called once on startup and
    /// whenever the culture changes. Results are cached after first load.
    /// </summary>
    public async Task LoadAsync(CultureInfo culture, CancellationToken ct = default)
    {
        var name = culture.TwoLetterISOLanguageName;

        if (_cache.TryGetValue(name, out var cached))
        {
            _current = cached;
            return;
        }

        var url = $"{_options.ResourcePath}/{name}.json";
        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, string>>(url, ct)
                       ?? [];
            _cache[name] = dict;
            _current     = dict;

            _logger.LogInformation("Loaded {Count} translations for [{Culture}]",
                dict.Count, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load translations from {Url}. Falling back to keys.", url);
            _current = [];
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void OnCultureChanged(object? sender, EventArgs e) =>
        _ = LoadAndNotifyAsync(CultureInfo.CurrentUICulture);

    private async Task LoadAndNotifyAsync(CultureInfo culture)
    {
        await LoadAsync(culture);
        LocalizationChanged?.Invoke(this, EventArgs.Empty);
    }
}
