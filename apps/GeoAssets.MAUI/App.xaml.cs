using GeoAssets.MAUI.Services;
using GeoAssets.MAUI.Services.Offline;

namespace GeoAssets.MAUI;

public partial class App : Application
{
    internal App(ConnectivitySyncTrigger connectivitySyncTrigger, OrderTypeCatalogLoader orderTypeCatalogLoader)
    {
        InitializeComponent();

        // XD01-276: starts the outbox flush-on-reconnect trigger once for the app's lifetime.
        connectivitySyncTrigger.Start();

        // XD01-378: the order types come from the server.
        orderTypeCatalogLoader.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage());
}
