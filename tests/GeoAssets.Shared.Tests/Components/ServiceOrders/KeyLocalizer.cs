using GeoAssets.Core.Localization;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>An <see cref="IJsonStringLocalizer"/> that answers every key with the key itself, so a render shows which key it used.</summary>
internal sealed class KeyLocalizer : IJsonStringLocalizer
{
    public string this[string key] => key;
    public string GetString(string key, params object[] args) => key;
    public event EventHandler LocalizationChanged { add { } remove { } }
}
