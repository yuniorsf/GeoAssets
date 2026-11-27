using GeoAssets.MAUI.Services.Map;

namespace GeoAssets.MAUI;

public partial class MainPage : ContentPage
{
    internal MainPage(MauiHostPage hostPage)
    {
        InitializeComponent();
        blazorWebView.HostPageRewrite = hostPage;
    }
}
