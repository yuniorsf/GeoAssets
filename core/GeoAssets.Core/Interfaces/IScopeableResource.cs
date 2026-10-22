namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Opt-in refinement of <see cref="IOrgOwnedResource"/> for a resource that can be grouped
/// into named subsets narrower than its whole type — e.g. "every <see cref="Models.GeoFeature"/>
/// belonging to município X," not every <see cref="Models.GeoFeature"/> the owning organization
/// has. <c>GeoAssets.Identity</c>'s <c>OrganizationGrant.ScopeId</c> matches against this when
/// set, letting a single grant cover one such subset instead of an entire <c>ResourceType</c>
/// (this project does not reference <c>GeoAssets.Identity</c>, hence the plain-text mention
/// rather than a <c>cref</c>). A resource type that does not implement this interface simply
/// cannot be granted at finer-than-<c>ResourceType</c> granularity — the pre-existing behavior
/// is unaffected (XD01-174).
/// </summary>
public interface IScopeableResource : IOrgOwnedResource
{
    /// <summary>
    /// The subset this resource instance belongs to, or <c>null</c> if it belongs to no named
    /// subset. Compared against the grant's <c>ScopeId</c> by exact, case-sensitive string match.
    /// </summary>
    string? ScopeId { get; }
}
