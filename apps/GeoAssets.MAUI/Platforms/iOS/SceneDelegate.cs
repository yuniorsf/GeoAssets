using Foundation;

namespace GeoAssets.MAUI;

/// <summary>
/// The window scene's delegate (XD01-461), named by <c>UIApplicationSceneManifest</c> in Info.plist: apps built with the
/// iOS 27 SDK fail to launch without the UIScene life cycle. Under it a URL opening the app reaches the scene, not
/// <see cref="AppDelegate.OpenUrl"/>, so <see cref="MauiProgram"/> hands the sign-in callback to MSAL.NET from the
/// scene's <c>SceneOpenUrl</c> life-cycle event too (XD01-52).
/// </summary>
[Register("SceneDelegate")]
internal sealed class SceneDelegate : MauiUISceneDelegate
{
}
