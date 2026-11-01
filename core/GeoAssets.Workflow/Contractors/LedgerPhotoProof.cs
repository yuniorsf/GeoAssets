using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Contractors;

/// <summary>One geotagged photo evidencing a <see cref="ContractorUsLedgerEntry"/>'s claimed work.</summary>
public sealed record LedgerPhotoProof(string Url, GeoPoint Location, DateTime CapturedAt);
