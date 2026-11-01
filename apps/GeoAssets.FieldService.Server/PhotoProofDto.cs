using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Contractors;

namespace GeoAssets.FieldService.Server;

/// <summary>Wire-contract photo proof — flattened lon/lat, same encoding as <c>ContractorUsLedgerMapper</c>'s persistence JSON.</summary>
internal sealed record PhotoProofDto(string Url, double Longitude, double Latitude, DateTime CapturedAt)
{
    public static PhotoProofDto FromDomain(LedgerPhotoProof photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return new(photo.Url, photo.Location.Longitude, photo.Location.Latitude, photo.CapturedAt);
    }

    public LedgerPhotoProof ToDomain() => new(Url, new GeoPoint(Longitude, Latitude), CapturedAt);
}
