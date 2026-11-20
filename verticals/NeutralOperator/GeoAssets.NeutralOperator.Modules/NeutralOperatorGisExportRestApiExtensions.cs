using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.GisExport;

namespace GeoAssets.NeutralOperator.Modules;

/// <summary>
/// The concessionaire GIS export route (XD01-271), moved out of the kernel's asset API (XD01-382, ADR-001 invariant I1):
/// it projects features through this vertical's <see cref="GisExportProjector"/>, so only a host that composes the
/// NeutralOperator module and serves an asset catalog of its own maps it — the kernel while its <c>GeoAssets:Modules</c>
/// lists <see cref="NeutralOperatorModule.ModuleId"/>. A kernel-only host does not expose it.
/// </summary>
public static class NeutralOperatorGisExportRestApiExtensions
{
    /// <summary>
    /// Generic concessionaire GIS export (XD01-271) — projects every feature of the given
    /// <c>assetTypeId</c> (the caller's own configured asset-type catalog has no
    /// fixed "pole" id, so the caller names which of their asset types that is) through
    /// <see cref="GisExportProjector"/> into the versioned contract documented on
    /// <see cref="GisExportSchema"/>, and returns it as plain RFC 7946 GeoJSON
    /// (<c>application/geo+json</c>) — consumable by GE Smallworld/Esri ArcGIS Enterprise or any
    /// other generic GIS import with zero GeoAssets-specific knowledge. No vendor-specific
    /// adapter is implemented here, by design (out of scope for this epic). Compliance-status
    /// fields report <see cref="GisExportSchema.ComplianceUnknown"/> for every feature until a
    /// persisted <see cref="PoleAttachmentSlotMatrix"/> store/DI registration exists — none does
    /// as of this ticket (see <see cref="NullPoleAttachmentSlotMatrixProvider"/>).
    /// </summary>
    public static IEndpointRouteBuilder MapNeutralOperatorGisExportApi(this IEndpointRouteBuilder routes, string prefix = "/api/geoassets")
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet($"{prefix}/export/gis/v1", (string assetTypeId, IAssetProvider provider) =>
        {
            var features = provider.GetByAssetType(assetTypeId);
            var collection = GisExportProjector.Project(features);
            return Results.Text(GisExportProjector.ToGeoJson(collection), "application/geo+json");
        })
            .RequireAuthorization("features:read");

        return routes;
    }
}
