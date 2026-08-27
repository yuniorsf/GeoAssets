using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Assets;

public partial class AssetForm
{
    [Parameter] public GeoFeature? Feature { get; set; }
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public EventCallback<GeoFeature> OnSave { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private string GeometryLabel => Feature?.Geometry switch
    {
        Core.Models.Geometry.GeoPoint      => L["map.draw.point"],
        Core.Models.Geometry.GeoLineString => L["map.draw.line"],
        Core.Models.Geometry.GeoPolygon    => L["map.draw.polygon"],
        _                                  => "—"
    };

    private IReadOnlyList<string> _attributeErrors = [];

    private async Task HandleSave()
    {
        if (Feature is null) return;
        var now = Clock.GetUtcNow().UtcDateTime;
        Feature.Properties.UpdatedAt = now;
        if (IsNew)
            Feature.Properties.CreatedAt = now;

        try
        {
            if (IsNew)
                Repository.Add(Feature);
            else
                Repository.Update(Feature);
        }
        catch (GeoFeatureAttributeValidationException ex)
        {
            _attributeErrors = ex.Errors;
            return;
        }

        _attributeErrors = [];
        await OnSave.InvokeAsync(Feature);
    }

    /// <summary>Allows a parent component to programmatically submit the form (e.g. from a context menu).</summary>
    public Task SaveAsync() => HandleSave();

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
