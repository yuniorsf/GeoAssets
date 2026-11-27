using GeoAssets.MAUI.Services.Map;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;

namespace GeoAssets.MAUI;

/// <summary>
/// The app's <see cref="BlazorWebView"/> (XD01-460): its static files as the platform serves them, the host page rewritten
/// by <see cref="HostPageRewrite"/>'s transform when it has one (MapLibre mode, <see cref="MauiHostPage"/>) — set before the
/// view is shown. Without one (the Leaflet modes) it is a plain <see cref="BlazorWebView"/>.
/// </summary>
public sealed class HostPageBlazorWebView : BlazorWebView
{
    /// <summary>How the host page is served; <see cref="MauiHostPage.Unchanged"/> until set.</summary>
    internal MauiHostPage HostPageRewrite { get; set; } = MauiHostPage.Unchanged;

    public override IFileProvider CreateFileProvider(string contentRootDir)
    {
        // The base is annotated supported from Android API 23, this app's AndroidMinSdkVersion; CA1416 reads the TFM's
        // floor (21) unless the call is guarded, and only on the Android compile (as MauiProgram's AddMauiBlazorWebView).
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            throw new PlatformNotSupportedException("The BlazorWebView needs Android API 23 or later.");
#endif
        var files = base.CreateFileProvider(contentRootDir);
        if (HostPageRewrite.Transform is not { } transform || HostPage is not { Length: > 0 } hostPage)
            return files;

        return new HostPageFileProvider(files, Path.GetRelativePath(Path.GetDirectoryName(hostPage) ?? string.Empty, hostPage), transform);
    }
}
