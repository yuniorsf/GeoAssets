using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Sources;

/// <summary>Scans a delivery folder for the layers a profile expects and checks each file set.</summary>
public interface ISourceInventory
{
    Task<DeliveryInventory> InspectAsync(string rootPath, ISourceProfile profile, CancellationToken cancellationToken);
}
