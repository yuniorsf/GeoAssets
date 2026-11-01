using GeoAssets.MAUI.Services.Offline;

namespace GeoAssets.MAUI;

public partial class App : Application
{
    internal App(ConnectivitySyncTrigger connectivitySyncTrigger)
    {
        InitializeComponent();

        // XD01-276: starts the outbox flush-on-reconnect trigger once for the app's lifetime.
        connectivitySyncTrigger.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage());
}
