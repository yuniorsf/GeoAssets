namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Marks a resource as owned by a single organization. <see cref="OrganizationId"/> is
/// non-nullable — <see cref="Guid.Empty"/> is the "no organization assigned" sentinel,
/// matching this codebase's existing sentinel-default convention (e.g.
/// <c>GeoFeatureProperties.CreatedAt</c>/<c>UpdatedAt</c> default to <c>DateTime.MinValue</c>
/// rather than being nullable).
/// </summary>
public interface IOrgOwnedResource
{
    Guid OrganizationId { get; }
}
