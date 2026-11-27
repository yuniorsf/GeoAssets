using Android.App;
using Android.Runtime;

namespace GeoAssets.MAUI;

[Application]
internal sealed class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
