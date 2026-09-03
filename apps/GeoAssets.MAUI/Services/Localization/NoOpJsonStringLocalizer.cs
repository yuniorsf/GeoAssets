using GeoAssets.Core.Localization;

namespace GeoAssets.MAUI.Services.Localization;

/// <summary>
/// <see cref="IJsonStringLocalizer"/> that returns every key unchanged — the same fallback
/// <c>HttpJsonStringLocalizer</c> (<c>GeoAssets.Shared</c>) already uses for a missing
/// translation, applied unconditionally. Stands in for the real MAUI localizer
/// <see cref="IJsonStringLocalizer"/>'s own doc comment anticipates ("MAUI (bundle file
/// loader)") until that's built (XD01-24): unblocks <c>LocalizedComponentBase</c>-derived
/// components (Service Orders, <c>CompleteProfile.razor</c>, <c>AssetsTable.razor</c>) so
/// they render in MAUI instead of throwing on a missing <see cref="IJsonStringLocalizer"/>
/// registration, at the cost of showing raw i18n keys instead of translated text.
/// </summary>
public sealed class NoOpJsonStringLocalizer : IJsonStringLocalizer
{
    public string this[string key] => key;

    public string GetString(string key, params object[] args) => key;

    public event EventHandler? LocalizationChanged;
}
