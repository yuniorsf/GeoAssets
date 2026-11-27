using Foundation;

namespace GeoAssets.MAUI;

/// <summary>
/// The window scene's delegate (XD01-461), named by <c>UIApplicationSceneManifest</c> in Info.plist: apps built with the
/// iOS/Mac Catalyst 27 SDK fail to launch without the UIScene life cycle.
/// </summary>
[Register("SceneDelegate")]
internal sealed class SceneDelegate : MauiUISceneDelegate
{
}
