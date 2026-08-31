using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Assets;

public partial class AssetForm
{
    [Parameter] public GeoFeature? Feature { get; set; }
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public EventCallback<GeoFeature> OnSave { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>
    /// v1 "snap-adjacent" radius for auto-linking (XD01-118) — ~11m at the equator. A tunable
    /// heuristic, not a spec'd value; not tied to the type-aware Geoman snapping XD01-119 will add.
    /// </summary>
    public const double SnapDistanceDegrees = 0.0001;

    private string GeometryLabel => Feature?.Geometry switch
    {
        Core.Models.Geometry.GeoPoint      => L["map.draw.point"],
        Core.Models.Geometry.GeoLineString => L["map.draw.line"],
        Core.Models.Geometry.GeoPolygon    => L["map.draw.polygon"],
        _                                  => "—"
    };

    private IReadOnlyList<string> _attributeErrors = [];

    private GeoFeature? _lastFeature;
    private GeoFeature? _autoLinkCandidate;
    private bool _autoLinkRejected;

    private AssetType? SelectedAssetType =>
        Feature is null ? null : Repository.GetAssetTypes().FirstOrDefault(t => t.Id.ToString() == Feature.Properties.AssetTypeId);

    private string SelectedAssetTypeSchema => SelectedAssetType?.AttributesSchemaJson ?? string.Empty;

    private bool HasSchema => !string.IsNullOrWhiteSpace(SelectedAssetTypeSchema);

    /// <summary>
    /// <see cref="_attributeErrors"/> not claimed by any field rendered by
    /// <see cref="SchemaDrivenAttributeEditor"/> (which shows its own matches inline) — the
    /// flat list below still needs to catch schema-level errors (e.g. a "required" violation,
    /// whose <c>InstanceLocation</c> points at the object root, not the missing property) and
    /// anything else that didn't map to a known field. Unfiltered when the type has no schema.
    /// </summary>
    private IReadOnlyList<string> UnmatchedAttributeErrors =>
        HasSchema
            ? [.. _attributeErrors.Where(e => !SchemaDrivenAttributeEditor.ParseFields(SelectedAssetTypeSchema)
                .Any(f => e.Contains(f.Key, StringComparison.OrdinalIgnoreCase)))]
            : _attributeErrors;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Feature, _lastFeature)) return;
        _lastFeature = Feature;
        _autoLinkRejected = false;
        _autoLinkCandidate = IsNew && Feature?.Geometry is GeoPoint point
            ? FindAutoLinkCandidate(point, Repository.GetNearby(point, SnapDistanceDegrees), Repository.GetIntersecting(point))
            : null;
    }

    /// <summary>
    /// v1 auto-link heuristic (XD01-118): a newly-placed <see cref="GeoPoint"/>-typed feature
    /// links to a nearby/intersecting <see cref="GeoLineString"/>-typed feature, but only when
    /// there's exactly one such candidate — 0 or 2+ candidates intentionally take no action (a
    /// "Connect to…" picker for the multi-candidate case is XD01-120, out of scope here). Static
    /// so it's directly unit-testable without rendering or an <c>IAssetProvider</c>.
    /// </summary>
    public static GeoFeature? FindAutoLinkCandidate(
        GeoGeometry? geometry, IReadOnlyList<GeoFeature> nearby, IReadOnlyList<GeoFeature> intersecting)
    {
        if (geometry is not GeoPoint) return null;

        var candidates = nearby.Concat(intersecting)
            .Where(f => f.Geometry is GeoLineString)
            .DistinctBy(f => f.Id)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private string AutoLinkCandidateName =>
        string.IsNullOrEmpty(_autoLinkCandidate?.Properties.Name) ? L["assets.noName"] : _autoLinkCandidate!.Properties.Name;

    private void RejectAutoLink() => _autoLinkRejected = true;

    private async Task HandleSave()
    {
        if (Feature is null) return;
        var now = Clock.GetUtcNow().UtcDateTime;
        Feature.Properties.UpdatedAt = now;
        if (IsNew)
        {
            Feature.Properties.CreatedAt = now;
            if (_autoLinkCandidate is not null && !_autoLinkRejected)
                Feature.Topology.Add(new TopoEdge { TargetId = _autoLinkCandidate.Id, Kind = "connected-to", Weight = 1.0 });
        }

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
