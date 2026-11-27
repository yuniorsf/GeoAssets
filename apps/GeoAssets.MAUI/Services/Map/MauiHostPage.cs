namespace GeoAssets.MAUI.Services.Map;

/// <summary>
/// How the BlazorWebView serves its host page (XD01-460): <see cref="Transform"/> rewrites it (MapLibre mode widens its
/// Content-Security-Policy, <see cref="MapLibreHostPage"/>); <see cref="Unchanged"/> — the Leaflet modes, the default —
/// serves the page exactly as bundled.
/// </summary>
internal sealed class MauiHostPage
{
    private MauiHostPage(Func<string, string>? transform) => Transform = transform;

    /// <summary>The page as bundled.</summary>
    public static MauiHostPage Unchanged { get; } = new(null);

    /// <summary>The page as <paramref name="transform"/> rewrites it.</summary>
    public static MauiHostPage RewrittenBy(Func<string, string> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return new MauiHostPage(transform);
    }

    /// <summary>The rewrite; <c>null</c> = none.</summary>
    public Func<string, string>? Transform { get; }
}
