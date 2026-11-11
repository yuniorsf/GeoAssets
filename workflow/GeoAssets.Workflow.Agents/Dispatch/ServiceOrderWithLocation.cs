using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>An open order's feature ID + resolved location, the minimal shape
/// <see cref="FeederClusteringService.Cluster"/> needs (XD01-240).</summary>
public sealed record ServiceOrderWithLocation(string OrderId, string FeatureId, GeoPoint Location);
