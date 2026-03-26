using GeoAssets.Core.Localization;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Localization;

/// <summary>
/// Base class for Blazor components that display localized text.
/// Automatically subscribes to <see cref="IJsonStringLocalizer.LocalizationChanged"/>
/// and calls <see cref="ComponentBase.StateHasChanged"/> so the component re-renders
/// whenever the active language changes — no manual wiring required.
///
/// <code>
/// @inherits LocalizedComponentBase
///
/// &lt;h1&gt;@L["app.title"]&lt;/h1&gt;
/// </code>
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject] protected IJsonStringLocalizer L { get; set; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        L.LocalizationChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, EventArgs e) =>
        InvokeAsync(StateHasChanged);

    public virtual void Dispose() =>
        L.LocalizationChanged -= OnLocalizationChanged;
}
