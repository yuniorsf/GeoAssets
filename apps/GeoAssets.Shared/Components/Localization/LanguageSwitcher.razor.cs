namespace GeoAssets.Shared.Components.Localization;

public partial class LanguageSwitcher
{
    private Task SelectAsync(string cultureName) =>
        Culture.SetCultureAsync(cultureName);
}
