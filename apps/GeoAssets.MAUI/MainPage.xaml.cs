using GeoAssets.MAUI.Services.Map;

namespace GeoAssets.MAUI;

internal sealed partial class MainPage : ContentPage
{
    internal MainPage(MauiHostPage hostPage)
    {
        InitializeComponent();
        blazorWebView.HostPageRewrite = hostPage;
    }
}
