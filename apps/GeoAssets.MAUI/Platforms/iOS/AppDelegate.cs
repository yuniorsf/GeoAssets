using Foundation;
using Microsoft.Identity.Client;
using UIKit;

namespace GeoAssets.MAUI;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Routes the system-browser sign-in callback (msauth.com.geoassets.app://auth, see
    // Platforms/iOS/Info.plist) back into MSAL.NET's pending AcquireTokenInteractive call
    // (XD01-52) — without this override, the browser completes sign-in but the app never
    // learns about it.
    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(url);
        return base.OpenUrl(app, url, options);
    }
}
