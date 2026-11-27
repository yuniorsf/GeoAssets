using GeoAssets.MAUI.Services;
using GeoAssets.MAUI.Services.Map;
using GeoAssets.MAUI.Services.Offline;

namespace GeoAssets.MAUI;

internal sealed partial class App : Application
{
    private readonly MauiHostPage _hostPage;

    public App(ConnectivitySyncTrigger connectivitySyncTrigger, OrderTypeCatalogLoader orderTypeCatalogLoader, MauiHostPage hostPage)
    {
        InitializeComponent();
        _hostPage = hostPage;

        // XD01-276: starts the outbox flush-on-reconnect trigger once for the app's lifetime.
        connectivitySyncTrigger.Start();

        // XD01-378: the order types come from the server.
        orderTypeCatalogLoader.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage(_hostPage));
}
