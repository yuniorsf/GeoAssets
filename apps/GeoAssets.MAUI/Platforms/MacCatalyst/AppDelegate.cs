using Foundation;
using UIKit;

namespace GeoAssets.MAUI;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Unlike Platforms/iOS/AppDelegate.cs (XD01-52), this deliberately does NOT forward to
    // Microsoft.Identity.Client.AuthenticationContinuationHelper: the pinned Microsoft.Identity.Client
    // 4.87.0 package ships no MacCatalyst-specific binding (only net8.0-android34.0/net8.0-ios18.0 —
    // NuGet resolves net10.0-maccatalyst to the plain net8.0 asset, which doesn't expose that
    // iOS-only type at all; confirmed via `dotnet build -f net10.0-maccatalyst`, which fails
    // CS0103 if this call is added here). Interactive sign-in on MacCatalyst is a known gap in
    // this ticket's scope until MSAL.NET ships MacCatalyst support — see the XD01-52 resolution
    // note.
}
