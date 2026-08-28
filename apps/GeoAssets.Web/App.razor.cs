namespace GeoAssets.Web;

public partial class App
{
    protected override async Task OnInitializedAsync()
    {
        // Resolve culture from localStorage / browser and load the first JSON file
        // before any component renders so there is no flash of untranslated keys.
        await CultureService.InitAsync();
        await Localizer.LoadAsync(CultureService.CurrentCulture);

        // Syncs C# state (topbar active-button display) to whatever the inline script in
        // index.html already applied to data-bs-theme before first paint — see
        // BlazorThemeService's doc comment for why the flash is actually prevented there,
        // not here.
        await ThemeService.InitAsync();
    }
}
